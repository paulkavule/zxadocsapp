// Quill-based rich-text editor for authoring/editing contract templates.
// Replaces the old contenteditable/execCommand helper. Supports formatting,
// tables (quill-table-better), and image editing (quill-resize-module). The
// Blazor component owns the container element; these functions create the Quill
// instance on it and read/write/format its content. Quill + plugins load from
// CDN (see App.razor); this module is loaded after them.
//
// The live instance is resolved via Quill.find(el) (Quill's own registry) rather
// than a custom expando — Blazor's re-render cycle can strip custom DOM
// properties, which would otherwise make getHtml return empty and lose content.
window.zxQuill = {
  registered: false,

  // Resolve the Quill instance for the container element.
  instance: function (el) {
    return el && window.Quill ? window.Quill.find(el) : null;
  },

  // Register quill-table-better once, before any instance is created.
  ensureRegistered: function () {
    if (this.registered) return;
    if (window.Quill && window.QuillTableBetter) {
      // register() wires up the table formats/blots and a modules/table entry,
      // but NOT modules/table-better — so also register the module under the
      // name our config references.
      if (window.QuillTableBetter.register) window.QuillTableBetter.register();
      window.Quill.register({ "modules/table-better": window.QuillTableBetter }, true);
      // Image editing: resize handles + align toolbar (quill-resize-module).
      // Its UMD exports a namespace object ({ default: ModuleClass }), so unwrap
      // .default — registering the object itself is not a constructor.
      if (window.QuillResize) {
        const Resize = window.QuillResize.default || window.QuillResize;
        window.Quill.register("modules/resize", Resize, true);
      }
      this.registered = true;
    }
  },

  // Create a Quill editor on el. The instance is retrievable via Quill.find(el).
  init: function (el, initialHtml) {
    if (!el || !window.Quill) return;
    if (window.Quill.find(el)) return; // already initialised
    this.ensureRegistered();

    const toolbar = [
      [{ header: [1, 2, 3, false] }],
      ["bold", "italic", "underline"],
      [{ list: "ordered" }, { list: "bullet" }],
      ["link", "image"],
    ];
    // The table button must be added to the toolbar explicitly; toolbarTable
    // alone does not inject it. Only add it when the plugin is present.
    if (window.QuillTableBetter) toolbar.push(["table-better"]);
    toolbar.push(["clean"]);

    const modules = {
      table: false, // disable Quill's minimal built-in table module
      toolbar,
    };

    // Enable full table editing when the plugin is present.
    if (window.QuillTableBetter) {
      modules["table-better"] = {
        language: "en_US",
        menus: ["column", "row", "merge", "table", "cell", "wrap", "copy", "delete"],
        toolbarTable: true,
      };
      modules.keyboard = { bindings: window.QuillTableBetter.keyboardBindings };
    }

    // Let the author edit an inserted image: drag to resize + align left/center/right.
    // embedTags must include IMG — the module defaults to VIDEO/IFRAME only.
    if (window.QuillResize) {
      modules.resize = {
        // Keyboard enables Delete/Backspace to remove a selected image; without
        // it, clicking an image shows the overlay but leaves no Quill selection.
        modules: ["Resize", "DisplaySize", "Toolbar", "Keyboard"],
        embedTags: ["IMG", "VIDEO", "IFRAME"],
      };
    }

    const quill = new window.Quill(el, { theme: "snow", modules });
    if (initialHtml) {
      quill.clipboard.dangerouslyPasteHTML(initialHtml);
    }
  },

  getHtml: function (el) {
    const quill = this.instance(el);
    return quill ? quill.root.innerHTML : "";
  },

  setHtml: function (el, html) {
    const quill = this.instance(el);
    if (quill) quill.clipboard.dangerouslyPasteHTML(html || "");
  },

  // Insert the token as plain text at the caret so {{key}} survives the merge.
  insertText: function (el, text) {
    const quill = this.instance(el);
    if (!quill) return;
    const range = quill.getSelection(true);
    const index = range ? range.index : quill.getLength() - 1;
    quill.insertText(index, text, "user");
    quill.setSelection(index + text.length, 0);
  },
};
