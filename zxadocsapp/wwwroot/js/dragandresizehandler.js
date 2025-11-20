function initializeDrag(containerId, divId, dotnetRef) {
  const target = document.getElementById(divId);
  const stage = document.getElementById(containerId);

  console.log("Element to initializeDrag = ", target);

  let pointerId = null;
  let offsetX = 0,
    offsetY = 0;

  function stageRect() {
    return stage.getBoundingClientRect();
  }

  // Create the resizable controller first
  const resizable = initResizable(
    stage,
    target,
    {
      maxW: 400,
      maxH: 300,
      contain: true, // keep inside stage for both drag & resize
    },
    dotnetRef
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
      const s = stageRect();
      const r = target.getBoundingClientRect();
      const x = r.left - s.left;
      const y = r.top - s.top;

      if (dotnetRef) dotnetRef.invokeMethodAsync("OnDragEnd", x, y);

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

function initResizable(stageEl, targetEl, options = {}, dotNetRef) {
  // options: { x, y, w, h, minW, minH, maxW, maxH, keepAspect, contain }
  const state = {
    x: options.x ?? 40,
    y: options.y ?? 40,
    w: options.w ?? 220,
    h: options.h ?? 140,
    minW: options.minW ?? 60,
    minH: options.minH ?? 40,
    maxW: options.maxW ?? Number.POSITIVE_INFINITY,
    maxH: options.maxH ?? Number.POSITIVE_INFINITY,
    keepAspect: !!options.keepAspect,
    contain: !!options.contain,
    aspect: null,
    pointerId: null,
    active: null, // which handle?
    startX: 0,
    startY: 0,
    startBox: null,
    raf: 0,
  };

  if (state.keepAspect) state.aspect = state.w / state.h;

  // Apply initial box style
  function applyStyle() {
    if (targetEl === undefined || targetEl == null) return;
    targetEl.style.transform = `translate(${Math.round(
      state.x
    )}px, ${Math.round(state.y)}px)`;
    targetEl.style.width = `${Math.round(state.w)}px`;
    targetEl.style.height = `${Math.round(state.h)}px`;
  }
  applyStyle();

  function clampBox(nx, ny, nw, nh) {
    // min/max
    nw = Math.max(state.minW, Math.min(nw, state.maxW));
    nh = Math.max(state.minH, Math.min(nh, state.maxH));

    if (state.contain && stageEl) {
      const s = stageEl.getBoundingClientRect();
      // Keep fully in stage bounds
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
        // if (dotNetRef) {
        //   dotNetRef
        //     .invokeMethodAsync("OnResize", state.x, state.y, state.w, state.h)
        //     .catch(() => {});
        // }
      });
    }
  }

  function onPointerDown(e) {
    const handle = e.currentTarget.dataset.handle; // "n","s","e","w","ne","nw","se","sw"
    if (!handle) return;
    if (e.pointerType === "mouse" && e.button !== 0) return;
    e.preventDefault();
    e.stopPropagation(); // IMPORTANT: prevents drag pointerdown from firing

    state.pointerId = e.pointerId;
    e.currentTarget.setPointerCapture(state.pointerId);
    state.active = handle;
    state.startX = e.clientX;
    state.startY = e.clientY;
    state.startBox = { x: state.x, y: state.y, w: state.w, h: state.h };
  }

  function onPointerMove(e) {
    if (state.pointerId === null || e.pointerId !== state.pointerId) return;
    const dx = e.clientX - state.startX;
    const dy = e.clientY - state.startY;

    let { x, y, w, h } = state.startBox;
    const hnd = state.active;

    // Horizontal adjustments
    if (hnd.includes("e")) w = state.startBox.w + dx;
    if (hnd.includes("w")) {
      w = state.startBox.w - dx;
      x = state.startBox.x + dx;
    }

    // Vertical adjustments
    if (hnd.includes("s")) h = state.startBox.h + dy;
    if (hnd.includes("n")) {
      h = state.startBox.h - dy;
      y = state.startBox.y + dy;
    }

    if (state.keepAspect && state.aspect) {
      // Adjust to maintain aspect; preference to the axis with larger change
      const viaWidthH = w / state.aspect; // height implied by width
      const viaHeightW = h * state.aspect; // width implied by height

      if (Math.abs(dx) > Math.abs(dy)) {
        // lock height from width
        let nh = viaWidthH;
        // If resizing from N, shift y to keep bottom anchored
        if (hnd.includes("n")) y = state.startBox.y + (state.startBox.h - nh);
        h = nh;
      } else {
        // lock width from height
        let nw = viaHeightW;
        // If resizing from W, shift x to keep right anchored
        if (hnd.includes("w")) x = state.startBox.x + (state.startBox.w - nw);
        w = nw;
      }
    }

    setBox(x, y, w, h);
  }

  function onPointerUp(e) {
    if (e.pointerId !== state.pointerId) return;
    e.currentTarget.releasePointerCapture(state.pointerId);
    state.pointerId = null;
    state.active = null;
    state.startBox = null;
    // if (dotNetRef) {
    //   dotNetRef
    //     .invokeMethodAsync("OnResizeEnd", state.x, state.y, state.w, state.h)
    //     .catch(() => {});
    // }
  }

  // Attach listeners to all handles inside targetEl
  const handles = targetEl.querySelectorAll("[data-handle]");
  handles.forEach((h) => {
    h.addEventListener("pointerdown", onPointerDown);
    h.addEventListener("pointermove", onPointerMove);
    h.addEventListener("pointerup", onPointerUp);
  });

  // Public API
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
        h.removeEventListener("pointermove", onPointerMove);
        h.removeEventListener("pointerup", onPointerUp);
      });
    },
  };
}
