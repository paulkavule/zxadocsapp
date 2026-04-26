function initializeDrag(
  containerId,
  divId,
  dotnetRef,
  defWeight = 220,
  defHeight = 60,
) {
  const target = document.getElementById(divId);
  console.log("Initializing drag for divId: " + divId);
  console.log("Target element found: ", target);

  if (target === undefined) return;
  const stage = document.getElementById(containerId);

  let pointerId = null;
  let offsetX = 0,
    offsetY = 0;

  function stageRect() {
    return stage.getBoundingClientRect();
  }
  console.log("Intialize drag completed successfull");
  // Create the resizable controller first
  const resizable = initResizable(
    stage,
    target,
    {
      x: 0,
      y: 0,
      w: defWeight,
      h: defHeight,
      maxW: 400,
      maxH: 300,
      contain: true, // keep inside stage for both drag & resize
    },
    dotnetRef,
  );

  function onPointerDown(e) {
    // Only left button for mouse, but allow touches
    if (e.pointerType === "mouse" && e.button !== 0) return;

    e.preventDefault();

    // NOTE: resize handles call stopPropagation() in initResizable,
    // so pointerdown on a handle will NOT reach here (no drag conflict)
    target.setPointerCapture(e.pointerId);
    pointerId = e.pointerId;

    const r = target.getBoundingClientRect();
    offsetX = e.clientX - r.left;
    offsetY = e.clientY - r.top;
  }

  function clamp(val, min, max) {
    return Math.max(min, Math.min(max, val));
  }

  function onPointerMove(e) {
    if (pointerId === null || e.pointerId !== pointerId) return;
    e.preventDefault();
    const s = stageRect();

    // Compute desired top-left of target within the stage
    let x = e.clientX - s.left - offsetX;
    let y = e.clientY - s.top - offsetY;

    // Current size from resizable state
    const box = resizable.getBox();
    const w = box.w;
    const h = box.h;

    // Keep the box fully inside the stage (canvas size)
    const maxX = s.width - w;
    const maxY = s.height - h;
    x = clamp(x, 0, maxX);
    y = clamp(y, 0, maxY);

    // Instead of directly setting transform, delegate to resizable,
    // so dragging and resizing use the same x/y/w/h
    resizable.setBox(x, y, w, h);
  }

  function onPointerUp(e) {
    if (e.pointerId === pointerId) {
      e.preventDefault();
      const s = stageRect();
      const r = target.getBoundingClientRect();
      const x = r.left - s.left;
      const y = r.top - s.top; // adjust for btns height
      console.log(
        "executing on drag " + divId,
        x,
        y,
        r.width,
        r.height,
        s.width,
        s.height,
      );
      if (dotnetRef) {
        dotnetRef.invokeMethodAsync(
          "OnDragEnd",
          divId,
          x,
          y,
          r.width,
          r.height,
          s.width,
          s.height,
        );
      }
      target.releasePointerCapture(pointerId);
      pointerId = null;
    }
  }

  target.addEventListener("pointerdown", onPointerDown);
  stage.addEventListener("pointermove", onPointerMove);
  window.addEventListener("pointerup", onPointerUp);

  // Public dispose to unhook everything (called from .NET)
  return {
    dispose() {
      target.removeEventListener("pointerdown", onPointerDown);
      stage.removeEventListener("pointermove", onPointerMove);
      window.removeEventListener("pointerup", onPointerUp);
      if (resizable && typeof resizable.dispose === "function") {
        resizable.dispose();
      }
    },
  };
}

function getTargetBtnsHeight(target) {
  if (!target) return 40;
  const targetBtns = target.querySelector("#target_btns");
  return targetBtns ? targetBtns.offsetHeight : 40;
}

function initResizable(stageEl, targetEl, options = {}, dotNetRef) {
  const state = {
    x: options.x ?? 40,
    y: options.y ?? 40,
    w: options.w ?? 220,
    h: options.h ?? 60,
    minW: options.minW ?? 60,
    minH: options.minH ?? 40,
    maxW: options.maxW ?? Number.POSITIVE_INFINITY,
    maxH: options.maxH ?? Number.POSITIVE_INFINITY,
    keepAspect: !!options.keepAspect,
    contain: !!options.contain,
    aspect: null,
    pointerId: null,
    active: null,
    startX: 0,
    startY: 0,
    startBox: null,
    raf: 0,
  };

  if (state.keepAspect) {
    state.aspect = state.w / state.h;
  }

  function applyStyle() {
    if (!targetEl) return;

    targetEl.style.transform = `translate(${Math.round(state.x)}px, ${Math.round(state.y)}px)`;
    targetEl.style.width = `${Math.round(state.w)}px`;
    targetEl.style.height = `${Math.round(state.h)}px`;
  }

  function clampBox(nx, ny, nw, nh) {
    nw = Math.max(state.minW, Math.min(nw, state.maxW));
    nh = Math.max(state.minH, Math.min(nh, state.maxH));

    if (state.contain && stageEl) {
      const s = stageEl.getBoundingClientRect();
      nx = Math.max(0, Math.min(nx, s.width - nw));
      ny = Math.max(0, Math.min(ny, s.height - nh));
    }

    return { x: nx, y: ny, w: nw, h: nh };
  }

  function setBox(nx, ny, nw, nh) {
    const c = clampBox(nx, ny, nw, nh);

    state.x = c.x;
    state.y = c.y;
    state.w = c.w;
    state.h = c.h;

    if (!state.raf) {
      state.raf = requestAnimationFrame(() => {
        state.raf = 0;
        applyStyle();
      });
    }
  }

  function onPointerDown(e) {
    const handle = e.currentTarget.dataset.handle;
    if (!handle) return;

    // Resize ONLY when left mouse button is pressed.
    // Hovering or moving over handles does nothing.
    if (e.pointerType === "mouse" && e.button !== 0) return;

    e.preventDefault();
    e.stopPropagation();

    state.pointerId = e.pointerId;
    state.active = handle;
    state.startX = e.clientX;
    state.startY = e.clientY;
    state.startBox = {
      x: state.x,
      y: state.y,
      w: state.w,
      h: state.h,
    };

    e.currentTarget.setPointerCapture(e.pointerId);

    window.addEventListener("pointermove", onPointerMove);
    window.addEventListener("pointerup", onPointerUp);
    window.addEventListener("pointercancel", onPointerUp);
  }

  function onPointerMove(e) {
    // No resizing unless pointerdown started from a handle.
    if (state.pointerId === null || e.pointerId !== state.pointerId) return;
    if (!state.startBox || !state.active) return;

    e.preventDefault();

    const dx = e.clientX - state.startX;
    const dy = e.clientY - state.startY;

    let { x, y, w, h } = state.startBox;
    const hnd = state.active;

    if (hnd.includes("e")) {
      w = state.startBox.w + dx;
    }

    if (hnd.includes("w")) {
      w = state.startBox.w - dx;
      x = state.startBox.x + dx;
    }

    if (hnd.includes("s")) {
      h = state.startBox.h + dy;
    }

    if (hnd.includes("n")) {
      h = state.startBox.h - dy;
      y = state.startBox.y + dy;
    }

    if (state.keepAspect && state.aspect) {
      const viaWidthH = w / state.aspect;
      const viaHeightW = h * state.aspect;

      if (Math.abs(dx) > Math.abs(dy)) {
        const nh = viaWidthH;
        if (hnd.includes("n")) {
          y = state.startBox.y + (state.startBox.h - nh);
        }
        h = nh;
      } else {
        const nw = viaHeightW;
        if (hnd.includes("w")) {
          x = state.startBox.x + (state.startBox.w - nw);
        }
        w = nw;
      }
    }

    setBox(x, y, w, h);
  }

  function onPointerUp(e) {
    if (state.pointerId === null || e.pointerId !== state.pointerId) return;

    e.preventDefault();

    state.pointerId = null;
    state.active = null;
    state.startBox = null;

    window.removeEventListener("pointermove", onPointerMove);
    window.removeEventListener("pointerup", onPointerUp);
    window.removeEventListener("pointercancel", onPointerUp);

    if (dotNetRef) {
      const s = stageEl.getBoundingClientRect();

      dotNetRef
        .invokeMethodAsync(
          "OnResizeEnd",
          targetEl.id,
          state.x,
          state.y,
          state.w,
          state.h,
          s.width,
          s.height,
        )
        .catch(() => {});
    }
  }

  applyStyle();

  const handles = targetEl.querySelectorAll("[data-handle]");

  handles.forEach((h) => {
    h.addEventListener("pointerdown", onPointerDown);

    // Optional: cursor only. No resizing happens on hover.
    h.style.touchAction = "none";
  });

  return {
    setBox(x, y, w, h) {
      setBox(x, y, w, h);
    },
    getBox() {
      return { x: state.x, y: state.y, w: state.w, h: state.h };
    },
    dispose() {
      handles.forEach((h) => {
        h.removeEventListener("pointerdown", onPointerDown);
      });

      window.removeEventListener("pointermove", onPointerMove);
      window.removeEventListener("pointerup", onPointerUp);
      window.removeEventListener("pointercancel", onPointerUp);
    },
  };
}
function resetCanvas(containerId) {
  const canvas = document.getElementById(containerId);
  const ctx = canvas.getContext("2d");
  ctx.clearRect(0, 0, canvas.width, canvas.height);
}
function getBoxRelativeToContainer(containerId, divId) {
  const container = document.getElementById(containerId);
  const target = document.getElementById(divId);
  if (!container || !target) return null;

  const cRect = container.getBoundingClientRect();
  const tRect = target.getBoundingClientRect();
  console.log(
    "Original values for target x: " +
      tRect.left +
      " y: " +
      tRect.top +
      " width: " +
      tRect.width +
      " height: " +
      tRect.height,
  );
  return {
    positionX: tRect.left - cRect.left,
    positionY: tRect.top - cRect.top,
    width: tRect.width,
    height: tRect.height,
  };
}
