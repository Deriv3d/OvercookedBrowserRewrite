#!/usr/bin/env python3
"""Bring an already-built phone build's offline copy up to date, without a
Unity build (the Home Screen app only ran while the PC served it).

Usage:
    py port\\mobile\\tools\\hotpatch_offline.py C:\\OVERCOOKED-LAB\\served\\phone7

Copies port/mobile/template/sw.js over the build's sw.js and applies the same
offline changes as the template to the build's index.html (only code with no
Unity template values in it, so the built page's values stay as they are).
Safe to run twice. The next phone build gets all of this from the template.
"""

import shutil
import sys
from pathlib import Path

TEMPLATE = Path(__file__).resolve().parent.parent / "template"

EDITS = [
    ('<div id="offline-note" class="hidden" style="opacity:.75">Saved on this device: it also opens with no connection.</div>',
     '<div id="offline-note" class="hidden" style="opacity:.75"></div>'),
    ('''        await cache.add(location.href.split("#")[0]);
        let dropped = 0;''',
     '''        await cache.add(location.href.split("#")[0]);
        // The page's own scripts: on the first visit they loaded before the
        // worker took over, so they were never stored, and offline the page
        // stopped at the missing saves.js.
        const pageFiles = ["saves.js", "manifest.json"].map((f) => new URL(f, location.href).href);
        await cache.addAll(pageFiles);
        for (const f of pageFiles) keep.add(f);
        let dropped = 0;'''),
    ('''        if (dropped) console.log("[page] offline copy: removed " + dropped + " file(s) of an older build");
      } catch (err) {''',
     '''        if (dropped) console.log("[page] offline copy: removed " + dropped + " file(s) of an older build");
        // Say "ready offline" only when every file of this build is stored.
        const missing = [];
        for (const url of keep) if (!(await cache.match(url))) missing.push(url.split("/").pop());
        const page = await cache.match(location.href.split("#")[0], { ignoreSearch: true });
        if (!page) missing.push("page");
        offlineNote.textContent = missing.length
          ? "Not ready for offline yet: open it once more while the PC is serving it."
          : "Ready to play with no connection.";
        offlineNote.classList.remove("hidden");
        console.log("[page] offline copy: " + (missing.length ? "missing " + missing.join(", ") : "complete"));
      } catch (err) {'''),
    ('''            if (offline === "on") {
              offlineNote.classList.remove("hidden");
              dropOldBuilds(script.src);''',
     '''            if (offline === "on" || (offline === "pending" && navigator.serviceWorker.controller)) {
              dropOldBuilds(script.src);'''),
]


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    build = Path(sys.argv[1])
    page = build / "index.html"
    if not page.exists():
        sys.exit(f"No index.html in {build}")
    text = page.read_text(encoding="utf-8")
    for old, new in EDITS:
        if new in text:
            continue
        if text.count(old) != 1:
            sys.exit(f"{page}: anchor not found once, nothing changed:\n{old.splitlines()[0]}")
        text = text.replace(old, new)
    page.write_text(text, encoding="utf-8")
    shutil.copyfile(TEMPLATE / "sw.js", build / "sw.js")
    print(f"Patched {page} and {build / 'sw.js'}.")


if __name__ == "__main__":
    main()
