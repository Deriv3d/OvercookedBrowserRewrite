// Simulated DualShock 4 for headless test runs (no real pad needed).
//
// Load it before the Unity loader on a test page. It replaces
// navigator.getGamepads() with one fake pad that Chrome would report for a
// DS4 v2 in the standard layout, so the game's WebGLDualShock4Profile should
// match it. It only runs when the page URL has ?pad=<steps>; otherwise it
// does nothing.
//
// Steps: comma-separated "<seconds after the game is ready>@<action>":
//   connect                 plug the pad in (fires gamepadconnected)
//   disconnect              unplug it
//   btn:<index>[:ms]        press and release a standard-layout button
//                           (0 Cross, 1 Circle, 2 Square, 3 Triangle, 4 L1, 5 R1,
//                           6 L2, 7 R2, 8 Share, 9 Options, 12-15 D-pad, 16 PS)
//   stick:<axis>:<value>[:ms]  hold an axis (0 LX, 1 LY, 2 RX, 3 RY; LY -1 = up),
//                           then return it to 0
// "Ready" is the moment the page sets window.unityInstance.
// Example: ?pad=1@connect,2@btn:0,30@stick:1:-1:1500,35@btn:9
// Every action is logged to the console as "[fake-pad] ...".

(function () {
  const steps = new URLSearchParams(location.search).get("pad");
  if (!steps) return;

  const BUTTONS = 18, AXES = 4;
  const pad = {
    id: "Wireless Controller (STANDARD GAMEPAD Vendor: 054c Product: 09cc)",
    index: 0,
    connected: false,
    mapping: "standard",
    timestamp: 0,
    axes: new Array(AXES).fill(0),
    buttons: Array.from({ length: BUTTONS }, () => ({ pressed: false, touched: false, value: 0 })),
    vibrationActuator: null,
  };

  function touch() { pad.timestamp = performance.now(); }
  function log(msg) { console.log("[fake-pad] " + msg); }

  navigator.getGamepads = function () {
    return pad.connected ? [pad, null, null, null] : [null, null, null, null];
  };

  function fire(type) {
    const ev = new Event(type);
    ev.gamepad = pad;
    window.dispatchEvent(ev);
  }

  function setButton(i, down) {
    const b = pad.buttons[i];
    b.pressed = b.touched = down;
    b.value = down ? 1 : 0;
    touch();
  }

  const actions = {
    connect() { pad.connected = true; touch(); fire("gamepadconnected"); log("connected " + pad.id); },
    disconnect() { pad.connected = false; touch(); fire("gamepaddisconnected"); log("disconnected"); },
    btn(index, ms) {
      const i = parseInt(index, 10), hold = parseInt(ms, 10) || 150;
      setButton(i, true);
      log("button " + i + " down for " + hold + " ms");
      setTimeout(() => { setButton(i, false); log("button " + i + " up"); }, hold);
    },
    stick(axis, value, ms) {
      const a = parseInt(axis, 10), v = parseFloat(value), hold = parseInt(ms, 10) || 500;
      pad.axes[a] = v; touch();
      log("axis " + a + " = " + v + " for " + hold + " ms");
      setTimeout(() => { pad.axes[a] = 0; touch(); log("axis " + a + " = 0"); }, hold);
    },
  };

  function run() {
    for (const step of steps.split(",").filter(Boolean)) {
      const at = step.indexOf("@");
      const seconds = parseFloat(step.slice(0, at));
      const [name, ...args] = step.slice(at + 1).split(":");
      if (!actions[name]) { log("unknown action " + name); continue; }
      setTimeout(() => actions[name](...args), seconds * 1000);
    }
    log("script started: " + steps);
  }

  const wait = setInterval(() => {
    if (window.unityInstance) { clearInterval(wait); run(); }
  }, 100);
})();
