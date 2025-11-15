// wwwroot/js/pdfjsInterop.js
window.blazorPdf = (function () {
  let state = {
    pdf: null,
    page: 1,
    scale: 1.2,
    container: null,
    canvas: null,
    ctx: null,
    dotnetRef: null,
  };

  async function renderPage(num) {
    if (!state.pdf) return;
    const page = await state.pdf.getPage(num);
    const viewport = page.getViewport({ scale: state.scale });
    state.canvas.width = viewport.width;
    state.canvas.height = viewport.height;
    console.log(viewport.height, viewport.width);
    await page.render({ canvasContext: state.ctx, viewport }).promise;
    state.page = num;
    if (state.dotnetRef)
      state.dotnetRef
        .invokeMethodAsync("OnPdfPageChanged", state.page)
        .catch(() => {});
  }
  function base64ToUint8Array(base64) {
    const raw = atob(base64);
    const uint8Array = new Uint8Array(raw.length);
    for (let i = 0; i < raw.length; i++) uint8Array[i] = raw.charCodeAt(i);
    return uint8Array;
  }
  function drawGrid(canvas) {
    const ctx = canvas.getContext("2d");
    const w = canvas.width;
    const h = canvas.height;

    ctx.clearRect(0, 0, w, h);

    // background
    ctx.fillStyle = "#fafafa";
    ctx.fillRect(0, 0, w, h);

    // grid
    ctx.beginPath();
    for (let x = 0; x <= w; x += 40) {
      ctx.moveTo(x + 0.5, 0);
      ctx.lineTo(x + 0.5, h);
    }
    for (let y = 0; y <= h; y += 40) {
      ctx.moveTo(0, y + 0.5);
      ctx.lineTo(w, y + 0.5);
    }
    ctx.strokeStyle = "#e0e0e0";
    ctx.lineWidth = 1;
    ctx.stroke();

    // border
    ctx.strokeStyle = "#bdbdbd";
    ctx.lineWidth = 2;
    ctx.strokeRect(1, 1, w - 2, h - 2);
  }

  return {
    init: async function (containerId, fileUrl, dotnetRef) {
      if (!window.pdfjsLib)
        throw new Error(
          "pdfjsLib not found. Ensure CDN 'pdf.min.js' is loaded before pdfjsInterop.js."
        );

      // Only set workerSrc if not set by _Host.cshtml
      if (!pdfjsLib.GlobalWorkerOptions.workerSrc) {
        pdfjsLib.GlobalWorkerOptions.workerSrc =
          "https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.worker.min.js";
      }

      const el = document.getElementById(containerId);
      if (!el) throw new Error("PDF container not found: " + containerId);
      el.innerHTML = "";

      const canvas = document.createElement("canvas");
      canvas.style.maxWidth = "100%";
      canvas.style.height = "auto";
      el.appendChild(canvas);

      state.container = el;
      state.canvas = canvas;
      state.ctx = canvas.getContext("2d");
      state.dotnetRef = dotnetRef;
      const fileBytes = base64ToUint8Array(fileUrl);
      console.log(">>>>>>>>><<<<<<<<<<>>>>>>>>>>>", fileBytes.length);
      state.pdf = await pdfjsLib.getDocument({ data: fileBytes }).promise;
      // state.pdf = await pdfjsLib.getDocument({ url: fileUrl }).promise;
      await renderPage(1);

      // draggable = document.createElement("div");
      // draggable.innerHTML = "Here I am";
      // draggable.style.top = "0";
      // draggable.style.left = "0";
      // draggable.style.position = "absolute";
      // el.appendChild(draggable);
      return { pageCount: state.pdf.numPages };
    },

    nextPage: () =>
      state.page < (state.pdf?.numPages ?? 0)
        ? renderPage(state.page + 1)
        : Promise.resolve(),
    prevPage: () =>
      state.page > 1 ? renderPage(state.page - 1) : Promise.resolve(),
    goToPage: (n) =>
      state.pdf
        ? renderPage(Math.max(1, Math.min(n, state.pdf.numPages)))
        : Promise.resolve(),
    zoomIn: () => {
      state.scale = Math.min(state.scale + 0.2, 3);
      return renderPage(state.page);
    },
    zoomOut: () => {
      state.scale = Math.max(state.scale - 0.2, 0.4);
      return renderPage(state.page);
    },
    dispose: () => {
      state = {
        pdf: null,
        page: 1,
        scale: 1.2,
        container: null,
        canvas: null,
        ctx: null,
        dotnetRef: null,
      };
    },
  };
})();
