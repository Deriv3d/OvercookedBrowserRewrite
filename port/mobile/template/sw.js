// Offline copy of the phone build (port/mobile/README.md, "Offline play").
// One cache for the site. The phone build uses hashed file names, so a new
// build's files are new entries; once a build has loaded, the page deletes
// the cached Build/ files it no longer uses. This file never needs to change
// between builds, so there is no worker update step to go wrong.
//
// - The page itself (navigations): network first, so a reachable server
//   always gives the newest build; the cached copy when offline, or when the
//   server doesn't answer within NAVIGATE_TIMEOUT_MS (a PC that is off or
//   away doesn't refuse the connection, it just never answers).
// - Everything else of this site (Build/*, StreamingAssets, manifest):
//   cache first, stored the first time it is fetched.
// - /__* (serve.py's log and test routes) and non-GET requests: never cached.
//
// Needs a secure page (https, or localhost on the PC); on plain http the page
// doesn't register it and loads from the network as before.
"use strict";

const CACHE = "overcooked-phone";
const NAVIGATE_TIMEOUT_MS = 4000;

self.addEventListener("install", () => self.skipWaiting());

self.addEventListener("activate", (event) => {
  event.waitUntil(self.clients.claim());
});

self.addEventListener("fetch", (event) => {
  const request = event.request;
  const url = new URL(request.url);
  if (request.method !== "GET" || url.origin !== self.location.origin || url.pathname.startsWith("/__")) return;
  // Range reads (audio/video) are answered by the network only.
  if (request.headers.has("range")) return;

  if (request.mode === "navigate") {
    event.respondWith((async () => {
      const cached = await caches.match(request, { ignoreSearch: true });
      try {
        const network = fetch(request);
        const response = cached
          ? await Promise.race([network, new Promise((_, no) => setTimeout(() => no(new Error("timeout")), NAVIGATE_TIMEOUT_MS))])
          : await network;
        if (response.ok) event.waitUntil(caches.open(CACHE).then((c) => c.put(request, response.clone())));
        return response;
      } catch (err) {
        if (cached) return cached;
        throw err;
      }
    })());
    return;
  }

  event.respondWith((async () => {
    const cache = await caches.open(CACHE);
    const cached = await cache.match(request);
    if (cached) return cached;
    const response = await fetch(request);
    // waitUntil: keep the worker alive until the copy is stored. Without it iOS
    // can stop the worker as soon as the page has the response, and a ~190 MB
    // data file is never saved (the Home Screen app only ran
    // while the PC served it).
    if (response.ok && response.status === 200) event.waitUntil(cache.put(request, response.clone()));
    return response;
  })());
});
