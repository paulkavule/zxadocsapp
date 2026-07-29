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
  // dotNet (optional) receives image uploads: Quill's default handler inlines a base64
  // data URI, so it is replaced with one that hands the file to .NET and inserts the
  // returned URL instead (ZD-84).
  init: function (el, initialHtml, dotNet) {
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

    // Route the toolbar's image button through .NET so the binary is uploaded rather than
    // embedded. Without a dotNet reference the default base64 behaviour is left alone.
    if (dotNet) {
      modules.toolbar = {
        container: toolbar,
        handlers: {
          image: () => this.pickAndUpload(el, dotNet),
        },
      };
    }

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

  // Max image width in px for an A4 page with 2.5cm margins (~16cm at 96dpi).
  maxImageWidth: 600,

  // Export the body for storage. Images get explicit width/height attributes because
  // LibreOffice's HTML import ignores CSS sizing (max-width, style width, a lone width
  // attribute) and places an image at its native pixel size — a phone photo then runs off
  // the page. Only width AND height together are honoured. Done on a clone so the live
  // editor is untouched.
  getHtml: function (el) {
    const quill = this.instance(el);
    if (!quill) return "";

    const clone = quill.root.cloneNode(true);
    const live = quill.root.querySelectorAll("img");
    clone.querySelectorAll("img").forEach((img, i) => {
      const source = live[i];
      if (!source || !source.naturalWidth || !source.naturalHeight) return;
      const width = Math.min(source.naturalWidth, this.maxImageWidth);
      img.setAttribute("width", Math.round(width));
      img.setAttribute("height", Math.round(source.naturalHeight * (width / source.naturalWidth)));
    });
    return clone.innerHTML;
  },

  setHtml: function (el, html) {
    const quill = this.instance(el);
    if (quill) quill.clipboard.dangerouslyPasteHTML(html || "");
  },

  // Prompt for a file and hand it to .NET as base64. The caret index is captured BEFORE
  // the await: the file dialog drops the editor selection, so reading it afterwards would
  // append the image at the end of the document instead of where the author was typing.
  pickAndUpload: function (el, dotNet) {
    const quill = this.instance(el);
    if (!quill) return;

    const range = quill.getSelection(true);
    const index = range ? range.index : quill.getLength() - 1;

    const input = document.createElement("input");
    input.type = "file";
    input.accept = "image/png,image/jpeg,image/gif,image/webp";
    input.onchange = async () => {
      const file = input.files && input.files[0];
      if (!file) return;
      const buffer = await file.arrayBuffer();
      const bytes = new Uint8Array(buffer);
      let binary = "";
      for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
      // .NET validates type/size and uploads; it calls back insertImage on success.
      await dotNet.invokeMethodAsync("UploadImageAsync", file.name, file.type, btoa(binary), index);
    };
    input.click();
  },

  // Place an uploaded image at the remembered caret position.
  insertImage: function (el, url, index) {
    const quill = this.instance(el);
    if (!quill) return;
    const at = typeof index === "number" ? Math.min(index, quill.getLength() - 1) : quill.getLength() - 1;
    quill.insertEmbed(at, "image", url, "user");
    quill.setSelection(at + 1, 0);
  },

  // Swap canonical image URLs for their signed, loadable equivalents (display only).
  applyDisplayUrls: function (el, pairs) {
    const quill = this.instance(el);
    if (!quill || !pairs) return;
    quill.root.querySelectorAll("img").forEach((img) => {
      const src = img.getAttribute("src") || "";
      const match = pairs.find((p) => src.includes("id=" + p.id));
      if (match) img.setAttribute("src", match.displayUrl);
    });
  },

  // Ids of images currently referenced by the content, so the caller can have them signed.
  imageIds: function (el) {
    const quill = this.instance(el);
    if (!quill) return [];
    const ids = [];
    quill.root.querySelectorAll("img").forEach((img) => {
      const m = /[?&](?:amp;)?id=([A-Za-z0-9-]+)/.exec(img.getAttribute("src") || "");
      if (m && !ids.includes(m[1])) ids.push(m[1]);
    });
    return ids;
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
