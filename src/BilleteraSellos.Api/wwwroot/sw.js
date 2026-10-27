/* Service worker de la billetera.
   - Guarda la "cáscara" de la app (páginas, scripts, íconos) para que abra rápido y sin conexión.
   - Las llamadas a /api NUNCA se guardan: tienen datos personales y tokens. Van siempre a la red,
     y si no hay conexión devuelven un error claro.
   Para publicar una versión nueva de la cáscara, cambiá VERSION. */
const VERSION = "billetera-v1";
const CASCARA = [
  "/",
  "/index.html",
  "/cartel.html",
  "/manifest.json",
  "/js/qr.js",
  "/lib/qrcode.min.js",
  "/lib/jsQR.min.js",
  "/iconos/icono-192.png",
  "/iconos/icono-512.png",
  "/iconos/apple-touch-icon.png",
  "/iconos/favicon-32.png",
];

self.addEventListener("install", (e) => {
  e.waitUntil(caches.open(VERSION).then((c) => c.addAll(CASCARA)).then(() => self.skipWaiting()));
});

self.addEventListener("activate", (e) => {
  e.waitUntil(
    caches.keys()
      .then((claves) => Promise.all(claves.filter((k) => k !== VERSION).map((k) => caches.delete(k))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener("fetch", (e) => {
  const req = e.request;
  const url = new URL(req.url);
  if (req.method !== "GET" && !url.pathname.startsWith("/api/")) return;
  if (url.origin !== self.location.origin) return;

  // API: solo red, jamás caché.
  if (url.pathname.startsWith("/api/")) {
    e.respondWith(
      fetch(req).catch(() =>
        new Response(JSON.stringify({ error: "No hay conexión. Revisá internet y probá de nuevo." }), {
          status: 503,
          headers: { "Content-Type": "application/json; charset=utf-8" },
        })
      )
    );
    return;
  }

  // Swagger y cualquier otra cosa fuera de la cáscara: sin intervenir.
  if (url.pathname.startsWith("/swagger")) return;

  // Páginas: primero la red (para tener siempre la última versión); sin conexión, la guardada.
  if (req.mode === "navigate") {
    e.respondWith(
      fetch(req)
        .then((r) => {
          if (r.ok) {
            const copia = r.clone();
            caches.open(VERSION).then((c) => c.put(url.pathname === "/" ? "/" : req, copia));
          }
          return r;
        })
        .catch(() => caches.match(req, { ignoreSearch: true }).then((r) => r || caches.match("/")))
    );
    return;
  }

  // Scripts, íconos, etc.: lo guardado al instante y se actualiza en segundo plano.
  e.respondWith(
    caches.match(req).then((guardado) => {
      const red = fetch(req)
        .then((r) => {
          if (r.ok && CASCARA.includes(url.pathname)) {
            const copia = r.clone();
            caches.open(VERSION).then((c) => c.put(req, copia));
          }
          return r;
        })
        .catch(() => guardado);
      return guardado || red;
    })
  );
});
