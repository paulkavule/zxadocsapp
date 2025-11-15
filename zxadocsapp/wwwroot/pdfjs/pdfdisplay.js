// wwwroot/js/pdfjsInterop.js
window.blazorPdf = (function () {
  let state = {
    pdf: null,
    page: 1,
    scale: 1.2, // logical (CSS) scale
    canvas: null,
    ctx: null,
    dotnetRef: null,
  };

  function resolveCanvas(canvasOrId) {
    if (!canvasOrId) throw new Error("Canvas not provided");
    if (typeof canvasOrId === "string") {
      const el = document.getElementById(canvasOrId);
      if (!el) throw new Error("Canvas not found: " + canvasOrId);
      if (el.tagName.toLowerCase() !== "canvas")
        throw new Error("Element is not a <canvas>: " + canvasOrId);
      return el;
    }
    // ElementReference from Blazor arrives as the element itself
    if (canvasOrId instanceof HTMLCanvasElement) return canvasOrId;
    // Some hosts pass a wrapper with a .id; try resolving that
    if (canvasOrId.id) {
      const el = document.getElementById(canvasOrId.id);
      if (el instanceof HTMLCanvasElement) return el;
    }
    throw new Error("Could not resolve canvas element");
  }

  function setupHiDPICanvas(canvas, widthCss, heightCss) {
    const dpr = window.devicePixelRatio || 1;
    // Backing store in device pixels
    canvas.width = Math.floor(widthCss * dpr);
    canvas.height = Math.floor(heightCss * dpr);
    // CSS size in CSS pixels
    canvas.style.width = `${Math.floor(widthCss)}px`;
    canvas.style.height = `${Math.floor(heightCss)}px`;
    const ctx = canvas.getContext("2d", { alpha: false });
    // Map 1 unit in canvas space to 1 CSS pixel
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    return ctx;
  }

  async function renderPage(num) {
    if (!state.pdf || !state.canvas) return;
    const page = await state.pdf.getPage(num);
    const viewport = page.getViewport({ scale: state.scale });

    // Ensure ctx uses correct DPR transform and canvas is sized correctly
    state.ctx = setupHiDPICanvas(state.canvas, viewport.width, viewport.height);

    await page.render({ canvasContext: state.ctx, viewport }).promise;
    state.page = num;

    if (state.dotnetRef) {
      state.dotnetRef
        .invokeMethodAsync("OnPdfPageChanged", state.page)
        .catch(() => {});
    }
  }

  return {
    /**
     * Initialize viewer to draw onto an existing <canvas>.
     * @param {string|HTMLCanvasElement} canvasOrId - canvas element or its id
     * @param {string} fileUrl - URL of the PDF
     * @param {any} dotnetRef - DotNetObjectReference for callbacks (optional)
     */
    init: async function (canvasOrId, fileUrl, dotnetRef) {
      if (!window.pdfjsLib)
        throw new Error(
          "pdfjsLib not found. Load pdf.min.js before pdfjsInterop.js."
        );

      // Only set workerSrc if not set by host page
      if (!pdfjsLib.GlobalWorkerOptions.workerSrc) {
        pdfjsLib.GlobalWorkerOptions.workerSrc =
          "https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.worker.min.js";
      }

      state.canvas = resolveCanvas(canvasOrId);
      state.ctx = state.canvas.getContext("2d");
      state.dotnetRef = dotnetRef ?? null;

      state.pdf = await pdfjsLib.getDocument({ url: fileUrl }).promise;
      await renderPage(1);
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

    /**
     * If you change the canvas CSS size externally, call this to re-render
     * at the same page/scale with updated backing resolution.
     */
    refresh: () => renderPage(state.page),

    dispose: () => {
      // Clear canvas and reset
      if (state.canvas && state.ctx) {
        state.ctx.setTransform(1, 0, 0, 1, 0, 0);
        state.ctx.clearRect(0, 0, state.canvas.width, state.canvas.height);
      }
      state = {
        pdf: null,
        page: 1,
        scale: 1.2,
        canvas: null,
        ctx: null,
        dotnetRef: null,
      };
    },
  };
})();
