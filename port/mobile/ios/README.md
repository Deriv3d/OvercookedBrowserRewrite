# iPhone app (port/mobile/ios)

This packs the phone build as a real iPhone app, installed with a free Apple ID through SideStore. It runs on the same engine as Safari (WKWebView), so speed and memory are the same as the Home Screen web app. What changes: it's a real app, it needs no certificate or PC once installed, and its saves live in the app's own storage.

## How it's made

1. **Shell (GitHub, no game files).** `Sources/*.swift` + `Info.plist`, built unsigned by `build_shell.sh` on a macOS runner (`.github/workflows/ios-shell.yml`) on every push that touches this folder. The run prints the shell as base64 with its SHA-256; it is copied from there to `dist/Overcooked-shell.ipa` (our own compiled code only, about 50 KB) and the hash checked.
2. **Pack (PC, lab only).** `py port\mobile\tools\pack_ipa.py C:\OVERCOOKED-LAB\served\phone7` adds the phone build as `Overcooked.app/Game/` and writes `C:\OVERCOOKED-LAB\served\phone7\app\Overcooked.ipa`. That file holds game files, so it never goes in the repo or on GitHub.
3. **Install (phone).** Download the .ipa from `http://<PC address>:8002/app/Overcooked.ipa` while 8002 serves phone7, then SideStore > My Apps > + . SideStore signs it with your Apple ID.

## What the shell does

| Part | Why |
|---|---|
| `BundleSchemeHandler` serves `app://localhost/...` from `Game/` | The page, its scripts and the build load from inside the app. Same scheme Cordova apps use. pack_ipa.py stores Build files unpacked, because a web view only unpacks gzip it gets over the network |
| Web view is first responder | WebKit only gives a page gamepads then (WebKit bug 269292) |
| Audio session `.playback` | Sound with the silent switch on, as the page asks Safari for |
| `alert`/`confirm` panels | Restore saves asks before replacing; without these a web view answers "no" |
| `overcookedShare` message | Back up saves opens the Share sheet (saves.js uses it when `window.webkit.messageHandlers.overcookedShare` exists) |
| Reload when the web process ends | Same as Safari reloading a tab iOS ended for memory |
| Landscape, no status bar, home bar hidden, edge swipes deferred | Full-screen game |

The page code is the phone page unchanged except what pack_ipa.py lists. The service worker doesn't run in the app (nothing needs it: every file is already on the phone).

## Saves

The app's saves start empty. Move them in with **Back up saves** where you play now (Safari link or Home Screen icon), then **Restore saves** in the app. saves.js moves the backup into the app's save folder (md5 of `app://localhost`). The old copies stay where they are.

Free Apple ID limits: the app runs 7 days per signing; SideStore refreshes it on the phone (LocalDevVPN on). An expired app keeps its saves; refreshing or reinstalling the same app keeps them. Deleting the app deletes them.
