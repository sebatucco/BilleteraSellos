/* Dibujo de códigos QR compartido por la billetera y el cartel del mostrador.
   Usa qrcode-generator (lib/qrcode.min.js), servido localmente para que funcione sin conexión. */
(function () {
  /**
   * Dibuja `texto` como QR en un <canvas>, en blanco y negro puro y con margen (zona silenciosa)
   * de 4 módulos, que es lo que necesitan los lectores para leerlo rápido.
   * @param {string} texto
   * @param {HTMLCanvasElement} canvas
   * @param {number} tamPx tamaño final en píxeles CSS
   */
  function dibujarQR(texto, canvas, tamPx) {
    const qr = qrcode(0, "M");
    qr.addData(texto);
    qr.make();
    const n = qr.getModuleCount(), margen = 4, total = n + margen * 2;
    const escala = window.devicePixelRatio || 1;
    // Cada módulo ocupa un número entero de píxeles para que no quede borroso.
    const modulo = Math.max(1, Math.floor((tamPx * escala) / total));
    const lado = modulo * total;
    canvas.width = lado;
    canvas.height = lado;
    canvas.style.width = tamPx + "px";
    canvas.style.height = tamPx + "px";
    canvas.style.imageRendering = "pixelated";
    const ctx = canvas.getContext("2d");
    ctx.fillStyle = "#fff";
    ctx.fillRect(0, 0, lado, lado);
    ctx.fillStyle = "#000";
    for (let f = 0; f < n; f++)
      for (let c = 0; c < n; c++)
        if (qr.isDark(f, c)) ctx.fillRect((c + margen) * modulo, (f + margen) * modulo, modulo, modulo);
    return canvas;
  }

  window.dibujarQR = dibujarQR;
})();
