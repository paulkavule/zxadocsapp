// wwwroot/dragOverlay.js

export function initDrag(stage, target, dotNetRef) {
  let pointerId = null;
  let offsetX = 0,
    offsetY = 0;

  // Make sure target isn't text-selectable or scroll-pannable
  target.style.touchAction = "none";

  function stageRect() {
    return stage.getBoundingClientRect();
  }

  function onPointerDown(e) {
    // Only left button for mouse, but allow touches
    if (e.pointerType === "mouse" && e.button !== 0) return;

    e.preventDefault();
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

    // Keep the box fully inside the stage (canvas size)
    const maxX = s.width - target.offsetWidth;
    const maxY = s.height - target.offsetHeight;
    x = clamp(x, 0, maxX);
    y = clamp(y, 0, maxY);

    // Smooth/stutter-free: only update transform
    target.style.transform = `translate(${Math.round(x)}px, ${Math.round(
      y
    )}px)`;
  }

  function onPointerUp(e) {
    if (e.pointerId === pointerId) {
      const s = stageRect();
      const r = target.getBoundingClientRect();
      const x = r.left - s.left;
      const y = r.top - s.top;

      if (dotNetRef) dotNetRef.invokeMethodAsync("OnDragEnd", x, y);

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
    },
  };
}

// Optional: draw a simple grid so you can see the canvas area
export function drawGrid(canvas) {
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
