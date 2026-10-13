// Logs each gamepad the browser exposes, once, as "[pad-log] ..." console lines:
// its name (id), layout (mapping), and button and axis counts. Unity's WebGL
// player uses that name as the joystick name, so this shows which pattern in
// WebGLStandardGamepadProfiles.cs a browser's pads need (Safari on iPhone
// names pads differently from Chrome on Windows).
// Include with <script src="pad-log.js"></script> on the game page. Costs
// nothing per frame: it only listens for connect and disconnect events.
(function () {
  if (!("getGamepads" in navigator)) {
    console.log("[pad-log] no Gamepad API in this browser");
    return;
  }
  function describe(pad) {
    return JSON.stringify(pad.id) + " mapping=" + JSON.stringify(pad.mapping) +
      " buttons=" + pad.buttons.length + " axes=" + pad.axes.length + " index=" + pad.index;
  }
  window.addEventListener("gamepadconnected", function (e) {
    console.log("[pad-log] connected " + describe(e.gamepad));
  });
  window.addEventListener("gamepaddisconnected", function (e) {
    console.log("[pad-log] disconnected " + describe(e.gamepad));
  });
  console.log("[pad-log] listening (" + navigator.userAgent + ")");
})();
