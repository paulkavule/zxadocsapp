// Scales a page-sized preview down to whatever width its pane happens to have (ZD-90).
//
// The preview iframe is laid out at the real page width so the document inside it reflows
// exactly as it will when printed — an image is the same fraction of the column here as in
// the PDF. That width is fixed, so the only way to fit it into a pane is to scale the whole
// sheet uniformly; a narrower iframe would reflow the content and defeat the point.
//
// Scale never exceeds 1: a pane wider than a page shows the page at full size, centred,
// rather than magnified.
window.zxPagePreview = {
  observed: new WeakMap(),

  attach: function (host, pageWidth, pageHeight) {
    if (!host || !pageWidth || !pageHeight || this.observed.has(host)) return;

    // Fit both axes so the whole page is visible at A4 proportions; the document scrolls
    // inside the sheet to reach later pages.
    const apply = () => {
      const w = host.clientWidth, h = host.clientHeight;
      if (!w || !h) return; // hidden pane — the observer fires again when it is shown
      host.style.setProperty("--zx-scale", Math.min(1, w / pageWidth, h / pageHeight));
    };

    apply();
    const observer = new ResizeObserver(apply);
    observer.observe(host);
    this.observed.set(host, observer);
  },

  detach: function (host) {
    const observer = this.observed.get(host);
    if (observer) {
      observer.disconnect();
      this.observed.delete(host);
    }
  },
};
