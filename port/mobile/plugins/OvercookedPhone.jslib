// Phone helpers for the optimisation framework (port/mobile/README.md).
// Read-only queries of the page plus the perf overlay; nothing here changes
// how the game plays.
mergeInto(LibraryManager.library, {
  // 1 on iPhone, iPad (iPadOS reports itself as a Mac with touch) and Android
  // phones; ?phone=0/1 on the page URL overrides the guess.
  OvercookedPhone_IsPhone: function () {
    var forced = new URLSearchParams(location.search).get("phone");
    if (forced === "0" || forced === "1") return forced === "1" ? 1 : 0;
    var ua = navigator.userAgent || "";
    if (/iPhone|iPad|iPod/.test(ua)) return 1;
    if (/Macintosh/.test(ua) && navigator.maxTouchPoints > 1) return 1;
    if (/Android/.test(ua) && /Mobile/.test(ua)) return 1;
    return 0;
  },

  // Integer value of a page URL parameter (?perf=2 -> 2, a bare ?perf -> 1),
  // or the fallback when it is missing or not a number.
  OvercookedPhone_QueryInt: function (namePtr, fallback) {
    var value = new URLSearchParams(location.search).get(UTF8ToString(namePtr));
    if (value === null) return fallback;
    if (value === "") return 1;
    var n = parseInt(value, 10);
    return isNaN(n) ? fallback : n;
  },

  // Size of the WebAssembly heap in MB: what Safari counts against the tab.
  OvercookedPhone_HeapMB: function () {
    return (HEAPU8.length / 1048576) | 0;
  },

  // One line of text in a small box over the top-left corner (?perf=2).
  OvercookedPhone_SetOverlay: function (textPtr) {
    var box = document.getElementById("webgl-port-perf");
    if (!box) {
      box = document.createElement("div");
      box.id = "webgl-port-perf";
      box.style.cssText = "position:fixed;left:calc(env(safe-area-inset-left) + 4px);top:calc(env(safe-area-inset-top) + 4px);" +
        "z-index:10;padding:2px 6px;background:rgba(0,0,0,.6);color:#fff;font:11px/1.3 ui-monospace,monospace;" +
        "white-space:pre;pointer-events:none";
      document.body.appendChild(box);
    }
    box.textContent = UTF8ToString(textPtr);
  },
});
