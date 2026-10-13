mergeInto(LibraryManager.library, {
  // Push the in-memory save files to IndexedDB (see WebGLPort.FlushSaves).
  // The game can save several files in a row; run one sync at a time and,
  // if more saves arrive meanwhile, one more sync afterwards, so nothing is
  // left unsynced and Emscripten doesn't warn about overlapping syncs.
  OvercookedPort_SyncFS: function () {
    var state = Module.overcookedSync || (Module.overcookedSync = { busy: false, again: false });
    if (state.busy) { state.again = true; return; }
    state.busy = true;
    FS.syncfs(false, function done(err) {
      if (err) console.warn("Overcooked: saving to browser storage failed", err);
      if (state.again) {
        state.again = false;
        FS.syncfs(false, done);
      } else {
        state.busy = false;
      }
    });
  },
});
