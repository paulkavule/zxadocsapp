// Minimal contenteditable rich-text helper for the contract template editor.
// No external dependency — uses document.execCommand for formatting. The Blazor
// component owns the element; these functions just read/write/format it.
window.zxRichText = {
  exec: function (el, cmd, val) {
    if (!el) return;
    el.focus();
    try {
      document.execCommand(cmd, false, val || null);
    } catch (e) {
      console.warn("richtext exec failed", cmd, e);
    }
  },
  getHtml: function (el) {
    return el ? el.innerHTML : "";
  },
  setHtml: function (el, html) {
    if (el) el.innerHTML = html || "";
  },
  insertText: function (el, text) {
    if (!el) return;
    el.focus();
    // insertText keeps the token as plain text so {{key}} survives the merge.
    try {
      document.execCommand("insertText", false, text);
    } catch (e) {
      console.warn("richtext insertText failed", e);
    }
  },
};
