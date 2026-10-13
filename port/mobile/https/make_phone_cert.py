#!/usr/bin/env python3
"""Make the home https certificate for the phone's offline Home Screen app.

Usage:
    py port\\mobile\\https\\make_phone_cert.py [folder] [extra IP or name ...]

Default folder: <lab>\\https (tools/portpaths.py). Default names: this PC's Wi-Fi
address, localhost, 127.0.0.1.

Safari only keeps an offline copy of the game (port/mobile/template/sw.js)
for an https address. This makes, with nothing to install (Python's standard
library only):

  ca.crt       "Overcooked Home CA", a private certificate authority. The phone
               installs and trusts it once (serve.py offers it at /__ca.crt).
  ca.key       its private key. Stays on the PC, in the lab folder; never in
               the repo, never sent anywhere.
  server.crt   the certificate serve.py shows, signed by the CA, for this PC's
               addresses. Valid 397 days (iPhones refuse longer ones for
               private CAs); run this again to renew. An existing CA is reused,
               so the phone doesn't need a new profile.
  server.key   its private key.

If the PC's Wi-Fi address changes, run it again (the new address goes into a
new server.crt) and restart serve.py.
"""

import base64
import datetime
import hashlib
import ipaddress
import math
import secrets
import socket
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[3] / "tools"))
import portpaths  # noqa: E402

DEFAULT_DIR = str(portpaths.https_dir())
CA_DAYS = 3650
SERVER_DAYS = 397
KEY_BITS = 2048


# --- RSA -------------------------------------------------------------------

SMALL_PRIMES = [p for p in range(3, 2000, 2) if all(p % q for q in range(3, int(p ** 0.5) + 1, 2))]


def is_probable_prime(n, rounds=40):
    if n < 2:
        return False
    for p in SMALL_PRIMES:
        if n % p == 0:
            return n == p
    d, r = n - 1, 0
    while d % 2 == 0:
        d //= 2
        r += 1
    for _ in range(rounds):
        a = secrets.randbelow(n - 3) + 2
        x = pow(a, d, n)
        if x in (1, n - 1):
            continue
        for _ in range(r - 1):
            x = pow(x, 2, n)
            if x == n - 1:
                break
        else:
            return False
    return True


def random_prime(bits, e):
    while True:
        # Top two bits set so p*q has the full key size; odd.
        n = secrets.randbits(bits) | (3 << (bits - 2)) | 1
        if math.gcd(n - 1, e) == 1 and is_probable_prime(n):
            return n


def new_rsa_key(bits=KEY_BITS):
    e = 65537
    while True:
        p, q = random_prime(bits // 2, e), random_prime(bits // 2, e)
        n = p * q
        if p != q and n.bit_length() == bits:
            break
    d = pow(e, -1, (p - 1) * (q - 1))
    return {"n": n, "e": e, "d": d, "p": p, "q": q,
            "dp": d % (p - 1), "dq": d % (q - 1), "qi": pow(q, -1, p)}


def sign_sha256(key, data):
    # RSASSA-PKCS1-v1_5 with SHA-256.
    digest_info = bytes.fromhex("3031300d060960864801650304020105000420") + hashlib.sha256(data).digest()
    k = (key["n"].bit_length() + 7) // 8
    em = b"\x00\x01" + b"\xff" * (k - len(digest_info) - 3) + b"\x00" + digest_info
    m = int.from_bytes(em, "big")
    # CRT, then checked against the public key.
    s1, s2 = pow(m, key["dp"], key["p"]), pow(m, key["dq"], key["q"])
    s = s2 + key["q"] * ((key["qi"] * (s1 - s2)) % key["p"])
    assert pow(s, key["e"], key["n"]) == m
    return s.to_bytes(k, "big")


# --- DER -------------------------------------------------------------------

def tlv(tag, body):
    n = len(body)
    if n < 0x80:
        length = bytes([n])
    else:
        raw = n.to_bytes((n.bit_length() + 7) // 8, "big")
        length = bytes([0x80 | len(raw)]) + raw
    return bytes([tag]) + length + body


def seq(*items):
    return tlv(0x30, b"".join(items))


def der_int(v):
    return tlv(0x02, v.to_bytes(v.bit_length() // 8 + 1, "big"))


def oid(dotted):
    parts = [int(x) for x in dotted.split(".")]
    body = bytes([40 * parts[0] + parts[1]])
    for p in parts[2:]:
        chunk = [p & 0x7F]
        p >>= 7
        while p:
            chunk.append(0x80 | (p & 0x7F))
            p >>= 7
        body += bytes(reversed(chunk))
    return tlv(0x06, body)


NULL = b"\x05\x00"
SHA256_RSA = seq(oid("1.2.840.113549.1.1.11"), NULL)


def bit_string(data):
    return tlv(0x03, b"\x00" + data)


def key_usage(bits):
    # DER: the unused-bits count must cover the trailing zero bits.
    unused = (bits & -bits).bit_length() - 1
    return tlv(0x03, bytes([unused, bits]))


def utf8(s):
    return tlv(0x0C, s.encode("utf-8"))


def time_der(t):
    if t.year < 2050:
        return tlv(0x17, t.strftime("%y%m%d%H%M%SZ").encode())
    return tlv(0x18, t.strftime("%Y%m%d%H%M%SZ").encode())


def name(cn, org="Overcooked Port"):
    return seq(
        tlv(0x31, seq(oid("2.5.4.10"), utf8(org))),
        tlv(0x31, seq(oid("2.5.4.3"), utf8(cn))),
    )


def public_key_info(key):
    rsa_public = seq(der_int(key["n"]), der_int(key["e"]))
    return seq(seq(oid("1.2.840.113549.1.1.1"), NULL), bit_string(rsa_public))


def key_id(key):
    return hashlib.sha1(seq(der_int(key["n"]), der_int(key["e"]))).digest()


def extension(dotted, value, critical=False):
    parts = [oid(dotted)]
    if critical:
        parts.append(b"\x01\x01\xff")
    parts.append(tlv(0x04, value))
    return seq(*parts)


def certificate(subject_cn, subject_key, issuer_cn, issuer_key, days, is_ca, alt_names=()):
    now = datetime.datetime.now(datetime.timezone.utc).replace(microsecond=0)
    exts = []
    if is_ca:
        exts.append(extension("2.5.29.19", seq(b"\x01\x01\xff"), critical=True))           # CA:true
        exts.append(extension("2.5.29.15", key_usage(0x06), critical=True))            # keyCertSign, cRLSign
    else:
        exts.append(extension("2.5.29.19", seq(), critical=True))                          # CA:false
        exts.append(extension("2.5.29.15", key_usage(0xA0), critical=True))            # digitalSignature, keyEncipherment
        exts.append(extension("2.5.29.37", seq(oid("1.3.6.1.5.5.7.3.1"))))                 # serverAuth
        general = []
        for alt in alt_names:
            try:
                general.append(tlv(0x87, ipaddress.ip_address(alt).packed))                # iPAddress
            except ValueError:
                general.append(tlv(0x82, alt.encode("ascii")))                             # dNSName
        exts.append(extension("2.5.29.17", seq(*general)))
    exts.append(extension("2.5.29.14", tlv(0x04, key_id(subject_key))))                   # subjectKeyIdentifier
    exts.append(extension("2.5.29.35", seq(tlv(0x80, key_id(issuer_key)))))               # authorityKeyIdentifier

    tbs = seq(
        tlv(0xA0, der_int(2)),                                                              # v3
        der_int(int.from_bytes(secrets.token_bytes(16), "big") >> 1),
        SHA256_RSA,
        name(issuer_cn),
        seq(time_der(now - datetime.timedelta(hours=1)), time_der(now + datetime.timedelta(days=days))),
        name(subject_cn),
        public_key_info(subject_key),
        tlv(0xA3, seq(*exts)),
    )
    return seq(tbs, SHA256_RSA, bit_string(sign_sha256(issuer_key, tbs)))


def private_key_der(key):
    return seq(*(der_int(v) for v in (0, key["n"], key["e"], key["d"], key["p"], key["q"], key["dp"], key["dq"], key["qi"])))


def pem(label, der):
    b64 = base64.encodebytes(der).decode().replace("\n", "")
    lines = [b64[i:i + 64] for i in range(0, len(b64), 64)]
    return "-----BEGIN %s-----\n%s\n-----END %s-----\n" % (label, "\n".join(lines), label)


def read_key(path):
    der = base64.b64decode("".join(l for l in path.read_text().splitlines() if not l.startswith("-----")))

    def read(i):
        tag, n = der[i], der[i + 1]
        i += 2
        if n & 0x80:
            k = n & 0x7F
            n = int.from_bytes(der[i:i + k], "big")
            i += k
        return tag, der[i:i + n], i + n

    _, body, _ = read(0)
    der = body
    values, i = [], 0
    while i < len(der):
        _, v, i = read(i)
        values.append(int.from_bytes(v, "big"))
    keys = ["version", "n", "e", "d", "p", "q", "dp", "dq", "qi"]
    return dict(zip(keys, values))


def lan_address():
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as probe:
        try:
            probe.connect(("192.168.0.1", 9))
            return probe.getsockname()[0]
        except OSError:
            return None


def main():
    args = sys.argv[1:]
    folder = Path(args[0] if args else DEFAULT_DIR)
    names = [a for a in [lan_address(), "localhost", "127.0.0.1"] + args[1:] if a]
    names = list(dict.fromkeys(names))
    folder.mkdir(parents=True, exist_ok=True)

    ca_crt, ca_key = folder / "ca.crt", folder / "ca.key"
    if ca_crt.exists() and ca_key.exists():
        ca = read_key(ca_key)
        print(f"Reusing the CA in {folder} (the phone keeps its profile).")
    else:
        print("Making the CA key (a few seconds)...")
        ca = new_rsa_key()
        ca_key.write_text(pem("RSA PRIVATE KEY", private_key_der(ca)))
        ca_crt.write_text(pem("CERTIFICATE", certificate("Overcooked Home CA", ca, "Overcooked Home CA", ca, CA_DAYS, True)))
        print(f"Wrote {ca_crt} and {ca_key}.")

    print("Making the server key...")
    server = new_rsa_key()
    (folder / "server.key").write_text(pem("RSA PRIVATE KEY", private_key_der(server)))
    (folder / "server.crt").write_text(pem("CERTIFICATE", certificate(names[0], server, "Overcooked Home CA", ca, SERVER_DAYS, False, names)))
    print(f"Wrote {folder / 'server.crt'} for: {', '.join(names)} (valid {SERVER_DAYS} days).")


if __name__ == "__main__":
    main()
