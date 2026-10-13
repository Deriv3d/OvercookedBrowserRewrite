// Save backup and restore for the phone page (port/mobile/README.md, "Saves").
//
// The game's saves and options live in the browser's IndexedDB, in the same
// site storage as the offline copy, so clearing one clears the other. This
// lets the player keep a copy anywhere they like (Files, iCloud Drive, AirDrop
// to the PC) and put it back later.
//
// Unity keeps Application.persistentDataPath (saves, PlayerPrefs) in
// Emscripten's IDBFS: database "/idbfs", store "FILE_DATA", one record per
// file or folder, keyed by its full path, value { timestamp, mode, contents }.
//
// A restore is never written while the game is running (the game would write
// its own copy back over it on the next save): it is parked in localStorage,
// the page reloads, and applyPendingRestore() writes it before Unity starts.
//
// Unity names the save folder /idbfs/<md5 of the page's folder address without
// the trailing slash>, e.g. md5("http://192.168.1.20:8002"). A backup made at
// one address (the plain http link) and restored at another (the https Home
// Screen app) is moved into this address's folder, or the game wouldn't find it.
"use strict";

window.OvercookedSaves = (() => {
  const DB = "/idbfs";
  const STORE = "FILE_DATA";
  const PENDING = "overcooked-pending-restore";
  const LAST_BACKUP = "overcooked-last-backup";

  function openDb() {
    return new Promise((resolve, reject) => {
      const req = indexedDB.open(DB);
      // Opening creates an empty database if there was none; give it the
      // store Emscripten expects so a first-run restore has somewhere to go.
      req.onupgradeneeded = () => { if (!req.result.objectStoreNames.contains(STORE)) req.result.createObjectStore(STORE); };
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => reject(req.error);
    });
  }

  function done(tx) {
    return new Promise((resolve, reject) => {
      tx.oncomplete = resolve;
      tx.onerror = () => reject(tx.error);
      tx.onabort = () => reject(tx.error);
    });
  }

  // MD5 (RFC 1321) of a string's UTF-8 bytes, as hex. Browsers' crypto.subtle
  // has no MD5, and it's only needed for Unity's folder name.
  function md5(text) {
    const bytes = new TextEncoder().encode(text);
    const words = new Uint32Array((((bytes.length + 8) >> 6) + 1) * 16);
    for (let i = 0; i < bytes.length; i++) words[i >> 2] |= bytes[i] << ((i % 4) * 8);
    words[bytes.length >> 2] |= 0x80 << ((bytes.length % 4) * 8);
    words[words.length - 2] = bytes.length * 8;
    const K = [], S = [7, 12, 17, 22, 5, 9, 14, 20, 4, 11, 16, 23, 6, 10, 15, 21];
    for (let i = 0; i < 64; i++) K[i] = Math.floor(Math.abs(Math.sin(i + 1)) * 2 ** 32) >>> 0;
    let a0 = 0x67452301, b0 = 0xefcdab89, c0 = 0x98badcfe, d0 = 0x10325476;
    for (let chunk = 0; chunk < words.length; chunk += 16) {
      let a = a0, b = b0, c = c0, d = d0;
      for (let i = 0; i < 64; i++) {
        let f, g;
        if (i < 16) { f = (b & c) | (~b & d); g = i; }
        else if (i < 32) { f = (d & b) | (~d & c); g = (5 * i + 1) % 16; }
        else if (i < 48) { f = b ^ c ^ d; g = (3 * i + 5) % 16; }
        else { f = c ^ (b | ~d); g = (7 * i) % 16; }
        const s = S[(i >> 4) * 4 + (i % 4)];
        const sum = (a + f + K[i] + words[chunk + g]) >>> 0;
        a = d; d = c; c = b;
        b = (b + ((sum << s) | (sum >>> (32 - s)))) >>> 0;
      }
      a0 = (a0 + a) >>> 0; b0 = (b0 + b) >>> 0; c0 = (c0 + c) >>> 0; d0 = (d0 + d) >>> 0;
    }
    return [a0, b0, c0, d0].map((v) => [0, 8, 16, 24].map((k) => ((v >>> k) & 0xff).toString(16).padStart(2, "0")).join("")).join("");
  }

  // This page's save folder, as Unity names it.
  // From the page's address up to its last "/" (Unity's folder rule). document.URL rather than location.origin, which may
  // read "null" for the iPhone app's app://localhost page (unverified).
  function saveFolder() {
    const url = document.URL;
    return DB + "/" + md5(url.slice(0, url.lastIndexOf("/")));
  }

  function toBase64(bytes) {
    let s = "";
    for (let i = 0; i < bytes.length; i += 0x8000) s += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
    return btoa(s);
  }

  function fromBase64(text) {
    const s = atob(text);
    const bytes = new Uint8Array(s.length);
    for (let i = 0; i < s.length; i++) bytes[i] = s.charCodeAt(i);
    return bytes;
  }

  async function readAll() {
    const db = await openDb();
    try {
      if (!db.objectStoreNames.contains(STORE)) return [];
      const tx = db.transaction(STORE, "readonly");
      const files = [];
      await new Promise((resolve, reject) => {
        const req = tx.objectStore(STORE).openCursor();
        req.onsuccess = () => {
          const c = req.result;
          if (!c) { resolve(); return; }
          const v = c.value || {};
          files.push({
            path: String(c.key),
            mode: v.mode,
            timestamp: v.timestamp ? new Date(v.timestamp).getTime() : Date.now(),
            contents: v.contents ? toBase64(new Uint8Array(v.contents)) : null,
          });
          c.continue();
        };
        req.onerror = () => reject(req.error);
      });
      return files;
    } finally {
      db.close();
    }
  }

  async function backup() {
    const files = await readAll();
    if (!files.some((f) => f.contents !== null)) throw new Error("There are no saves on this device yet.");
    const stamp = new Date().toISOString().slice(0, 16).replace(/[-:]/g, "").replace("T", "-");
    const name = "overcooked-saves-" + stamp + ".json";
    const json = JSON.stringify({ format: "overcooked-saves", version: 1, exported: new Date().toISOString(), files });
    const file = new File([json], name, { type: "application/json" });
    // The iPhone app (port/mobile/ios) opens the Share sheet itself: its web
    // view has no reliable navigator.share or download.
    const app = window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.overcookedShare;
    if (app) {
      if (!(await app.postMessage({ name, text: json }))) {
        const closed = new Error("Share sheet closed");
        closed.name = "AbortError";
        throw closed;
      }
    } else if (navigator.canShare && navigator.canShare({ files: [file] })) {
      // iPhone: the Share sheet (Save to Files, iCloud Drive, AirDrop, Mail).
      await navigator.share({ files: [file], title: "Overcooked saves" });
    } else {
      const a = document.createElement("a");
      a.href = URL.createObjectURL(file);
      a.download = name;
      document.body.appendChild(a);
      a.click();
      setTimeout(() => { URL.revokeObjectURL(a.href); a.remove(); }, 1000);
    }
    try { localStorage.setItem(LAST_BACKUP, String(Date.now())); } catch (err) { /* storage full or blocked */ }
    return files.filter((f) => f.contents !== null).length;
  }

  // Checks the file and parks it for the next page load. Returns the number of files in it.
  async function stageRestore(file) {
    let data;
    try { data = JSON.parse(await file.text()); } catch (err) { throw new Error("That file isn't an Overcooked save backup."); }
    if (!data || data.format !== "overcooked-saves" || !Array.isArray(data.files)) throw new Error("That file isn't an Overcooked save backup.");
    if (!data.files.every((f) => typeof f.path === "string" && f.path.startsWith(DB + "/"))) throw new Error("That backup has files outside the save folder; not restored.");
    localStorage.setItem(PENDING, JSON.stringify(data));
    return data.files.filter((f) => f.contents !== null).length;
  }

  // Called by the page before Unity starts. Replaces the saves with the parked backup.
  async function applyPendingRestore() {
    let text = null;
    try { text = localStorage.getItem(PENDING); } catch (err) { return null; }
    if (!text) return null;
    const data = JSON.parse(text);
    const db = await openDb();
    try {
      const tx = db.transaction(STORE, "readwrite");
      const store = tx.objectStore(STORE);
      store.clear();
      const folder = saveFolder();
      for (const f of data.files) {
        // Into this address's folder (see the top of this file).
        const path = f.path.replace(/^\/idbfs\/[0-9a-f]{32}(?=\/|$)/, folder);
        const value = { timestamp: new Date(f.timestamp), mode: f.mode };
        if (f.contents !== null) value.contents = fromBase64(f.contents);
        store.put(value, path);
      }
      await done(tx);
    } finally {
      db.close();
    }
    localStorage.removeItem(PENDING);
    return data.exported || "unknown date";
  }

  function lastBackup() {
    try { return +localStorage.getItem(LAST_BACKUP) || 0; } catch (err) { return 0; }
  }

  return { backup, stageRestore, applyPendingRestore, lastBackup, md5, saveFolder };
})();
