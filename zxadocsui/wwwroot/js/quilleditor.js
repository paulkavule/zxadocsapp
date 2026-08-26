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

  // The .NET component behind each editor, so any function here can call back without the
  // reference being threaded through its arguments. A WeakMap rather than a property on the
  // element, for the same reason the instance is resolved via Quill.find: Blazor's re-render
  // cycle can strip custom DOM properties.
  dotNetRefs: new WeakMap(),

  // Resolve the Quill instance for the container element.
  instance: function (el) {
    return el && window.Quill ? window.Quill.find(el) : null;
  },

  // The .NET component for this editor, or null when it was created without one.
  dotNet: function (el) {
    return (el && this.dotNetRefs.get(el)) || null;
  },

  // Register quill-table-better once, before any instance is created.
  ensureRegistered: function () {
    if (this.registered) return;
    if (window.Quill && window.QuillTableBetter) {
      // Text alignment as an inline style, not Quill's default ql-align-* class (ZD-95).
      // Those classes are defined only in quill.snow.css, which loads on the authoring page
      // alone — a class-aligned paragraph would render centred in the editor and silently
      // flatten to left in every preview and in the PDF. The style travels with the markup.
      window.Quill.register(window.Quill.import("attributors/style/align"), true);
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
  // contentWidth is the printable width in CSS px, passed from PageGeometry so the editor
  // and the PDF share one number instead of each assuming a page (ZD-90).
  init: function (el, initialHtml, dotNet, contentWidth, initialDelta) {
    if (!el || !window.Quill) return;
    if (window.Quill.find(el)) return; // already initialised
    this.ensureRegistered();

    if (contentWidth > 0) el.dataset.zxContentWidth = contentWidth;
    if (dotNet) this.dotNetRefs.set(el, dotNet);

    const toolbar = [
      [{ header: [1, 2, 3, false] }],
      ["bold", "italic", "underline"],
      [{ list: "ordered" }, { list: "bullet" }],
      // Separate buttons rather than the {align: []} dropdown: three one-click controls the
      // author can see the state of. The empty value is left, and clears the style.
      [{ align: "" }, { align: "center" }, { align: "right" }],
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
          image: () => this.pickAndUpload(el),
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

    new window.Quill(el, { theme: "snow", modules });
    // Prefer the Delta: it is the editor's own representation and needs no conversion. The HTML is
    // the fallback for documents saved before the Delta was stored, or if the stored payload is
    // unreadable — and it goes through setHtml, so such a document's tables come back too.
    if (!this.setDelta(el, initialDelta) && initialHtml) {
      this.setHtml(el, initialHtml);
    }
  },

  // Printable width in CSS px, supplied by PageGeometry at init. There is deliberately no
  // fallback: a literal here would be a second source of truth for the page, and the previous
  // one (a hardcoded 600px image cap chosen to approximate A4 minus margins) is exactly the
  // defect this change set removed. init always supplies the value, so its absence is a wiring
  // bug worth hearing about rather than papering over with a page-shaped guess.
  contentWidth: function (el) {
    const declared = el && Number(el.dataset.zxContentWidth);
    if (declared > 0) return declared;
    throw new Error("zxQuill: no content width — zxQuill.init was not given PageGeometry's");
  },

  // Export the body for storage. Images get explicit width/height attributes because
  // LibreOffice's HTML import ignores CSS sizing (max-width, style width, a lone width
  // attribute) and places an image at its native pixel size — a phone photo then runs off
  // the page. Only width AND height together are honoured. Done on a clone so the live
  // editor is untouched.
  //
  // The exported width is the width the author is LOOKING at (the laid-out box), not the
  // image's native size: quill-resize-module lets them scale an image down, and stamping the
  // native size instead would print something larger than the editor ever showed. The height
  // always comes from the natural aspect ratio, so a resize can never distort the image.
  getHtml: function (el) {
    const quill = this.instance(el);
    if (!quill) return "";

    const max = this.contentWidth(el);
    const clone = quill.root.cloneNode(true);
    const live = quill.root.querySelectorAll("img");
    clone.querySelectorAll("img").forEach((img, i) => {
      const source = live[i];
      if (!source || !source.naturalWidth || !source.naturalHeight) return;
      const displayed = Math.round(source.getBoundingClientRect().width) || source.naturalWidth;
      const width = Math.min(displayed, max);
      img.setAttribute("width", width);
      img.setAttribute("height", Math.round(source.naturalHeight * (width / source.naturalWidth)));
    });

    // Column ratios are measured off the live table and stamped as percentage width attributes on
    // the FIRST ROW's cells, because the editor is the only place they are known — the table plugin
    // renders no <colgroup>, so LibreOffice apportioned columns by content and printed ratios the
    // screen never showed (measured 28.7/71.3 against the editor's 50/50).
    //
    // The form matters, and was measured against real conversions for a 50/50 table:
    //   <td width="50%">          -> 50.0 / 50.0   exact
    //   <col width="227">         -> 49.8 / 50.2
    //   <col width="50%">         -> 46.5 / 53.4   and it OVERRIDES the cells, so no colgroup
    const liveTables = quill.root.querySelectorAll("table");
    clone.querySelectorAll("table").forEach((table, i) => {
      const source = liveTables[i];
      const liveRow = source && source.querySelector("tr");
      const cloneRow = table.querySelector("tr");
      if (!liveRow || !cloneRow) return;

      const widths = [...liveRow.children].map((c) => c.getBoundingClientRect().width);
      const total = widths.reduce((a, b) => a + b, 0);
      if (!total) return;

      [...cloneRow.children].forEach((cell, c) => {
        if (widths[c] === undefined || cell.hasAttribute("width")) return;
        cell.setAttribute("width", Math.round((widths[c] / total) * 10000) / 100 + "%");
      });
    });

    return clone.innerHTML;
  },

  // Load HTML by converting it to a Delta and APPLYING it, never via dangerouslyPasteHTML.
  // Measured on the same imported document: dangerouslyPasteHTML produced a table element with
  // 0 rows, while convert + updateContents produced all 3 rows and 6 cells with one shared
  // table id. It is the same asymmetry setDelta documents — the table plugin builds its blots on
  // the insert path, and dangerouslyPasteHTML (setContents underneath) does not take it.
  setHtml: function (el, html) {
    const quill = this.instance(el);
    if (!quill) return;
    const delta = quill.clipboard.convert({ html: html || "" });
    quill.setText("");
    quill.updateContents(delta, "api");
  },

  // The editor's own lossless representation. Used for re-opening a saved document: a table does
  // not survive an HTML round trip through quill-table-better, but the Delta reproduces exactly
  // what was authored.
  getDelta: function (el) {
    const quill = this.instance(el);
    return quill ? JSON.stringify(quill.getContents()) : "";
  },

  // Applied as an UPDATE onto an emptied document, never via setContents. Measured with one and
  // the same delta: setContents rebuilds the table element but none of its rows (0 rows, 0 cells),
  // while updateContents restores it in full (3 rows, 9 cells) — the table plugin builds its blots
  // on the insert path that setContents does not take.
  setDelta: function (el, json) {
    const quill = this.instance(el);
    if (!quill || !json) return false;
    try {
      const delta = JSON.parse(json);
      quill.setText("");
      quill.updateContents(delta, "api");
      return true;
    } catch (e) {
      return false; // caller falls back to the body HTML
    }
  },

  // Prompt for a file and hand it to .NET as base64. The caret index is captured BEFORE
  // the await: the file dialog drops the editor selection, so reading it afterwards would
  // append the image at the end of the document instead of where the author was typing.
  pickAndUpload: function (el) {
    const quill = this.instance(el);
    const dotNet = this.dotNet(el);
    if (!quill || !dotNet) return;

    const range = quill.getSelection(true);
    const index = range ? range.index : quill.getLength() - 1;

    const input = document.createElement("input");
    input.type = "file";
    input.accept = "image/png,image/jpeg,image/gif,image/webp";
    input.onchange = async () => {
      const file = input.files && input.files[0];
      if (!file) return;
      // .NET validates type/size, uploads, and returns the URL to display — or null, having
      // already told the user why. Inserting is this caller's job, because the import path
      // uses the very same upload to rewrite an <img> src instead.
      const url = await this.upload(dotNet, file.name, file.type, await this.toBase64(file));
      if (url) this.insertImage(el, url, index);
    };
    input.click();
  },

  // Bytes as base64, which is how an image crosses the Blazor interop boundary. SignalR's
  // MaximumReceiveMessageSize is raised to 8MB in Program.cs for exactly this; the real ceiling
  // is the server's Templates:MaxImageFileMb check.
  toBase64: async function (blob) {
    const bytes = new Uint8Array(await blob.arrayBuffer());
    let binary = "";
    // Chunked: one spread of a multi-megabyte array blows the argument limit.
    for (let i = 0; i < bytes.length; i += 8192)
      binary += String.fromCharCode.apply(null, bytes.subarray(i, i + 8192));
    return btoa(binary);
  },

  upload: function (dotNet, fileName, contentType, base64) {
    return dotNet.invokeMethodAsync("UploadImageAsync", fileName, contentType, base64);
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

  // ---------------------------------------------------------------- import (ZD-85)
  //
  // Conversion happens HERE, in the browser. The API is asked for nothing new: extracted
  // images go through the same upload the toolbar image button uses, and the markup enters
  // through the same paste path that saved content uses — so Quill's clipboard is what
  // constrains it. Anything it has no blot for, scripts and event handlers included, is
  // dropped on the way in, and what is later stored is the editor's own export rather than
  // the converter's output.
  //
  // The document itself never crosses the Blazor interop boundary; only the images do, at
  // the same size limit as a manually inserted one.

  MAMMOTH_SRC: "/lib/mammoth/mammoth.browser.min.js",

  // 620KB, so it is fetched on first import instead of on every page load.
  loadMammoth: async function () {
    if (window.mammoth) return window.mammoth;

    await new Promise((resolve, reject) => {
      const tag = document.createElement("script");
      tag.src = this.MAMMOTH_SRC;
      tag.onload = resolve;
      tag.onerror = () => reject(new Error("Could not load the Word converter."));
      document.head.appendChild(tag);
    });

    if (!window.mammoth) throw new Error("The Word converter loaded but did not initialise.");
    return window.mammoth;
  },

  // Prompt for a document, then hand it to importDocument. Split so a test can drive the
  // conversion with a fixture: a native file dialog cannot be automated.
  pickAndImport: function (el) {
    const input = document.createElement("input");
    input.type = "file";
    input.accept = ".docx,.pdf";
    input.onchange = async () => {
      const file = input.files && input.files[0];
      if (file) await this.importDocument(el, file.name, await this.toBase64(file));
    };
    input.click();
  },

  // Returns true when content was imported, false when the author declined or it failed.
  importDocument: async function (el, fileName, base64) {
    const quill = this.instance(el);
    const dotNet = this.dotNet(el);
    if (!quill || !dotNet) return false;

    // Replacing the body is destructive, so ask first — but only when there is something to
    // lose. The dialog is .NET's, to match every other prompt in the app.
    if (!this.isEmpty(el) && !(await dotNet.invokeMethodAsync("ConfirmImportAsync"))) return false;

    try {
      const bytes = this.fromBase64(base64);
      const isPdf = /\.pdf$/i.test(fileName || "");
      const html = isPdf ? await this.pdfToHtml(bytes) : await this.docxToHtml(bytes);

      const withImages = await this.uploadEmbeddedImages(dotNet, html);
      this.setHtml(el, withImages);
      return true;
    } catch (e) {
      await dotNet.invokeMethodAsync("ImportFailedAsync", e && e.message ? e.message : String(e));
      return false;
    }
  },

  // An editor holding only the trailing newline Quill always keeps.
  isEmpty: function (el) {
    const quill = this.instance(el);
    return !quill || quill.getLength() <= 1;
  },

  fromBase64: function (base64) {
    const binary = atob(base64 || "");
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    return bytes;
  },

  // mammoth maps Word styles to semantic HTML: headings, lists, bold/italic, tables. Images
  // come back as data URIs, which uploadEmbeddedImages then exchanges for backend URLs — no
  // base64 may reach storage (ZD-82).
  docxToHtml: async function (bytes) {
    const mammoth = await this.loadMammoth();
    const result = await mammoth.convertToHtml({ arrayBuffer: bytes.buffer });
    return (result && result.value) || "";
  },

  // pdf.js is already loaded for the document viewer. A PDF carries no structure, only
  // positioned text runs, so paragraphs are rebuilt from geometry: a new line when the
  // baseline moves, a new paragraph when it moves by more than a line and a half. Tables and
  // columns cannot survive this and are not attempted.
  pdfToHtml: async function (bytes) {
    if (!window.pdfjsLib) throw new Error("The PDF reader is not available on this page.");

    const pdf = await window.pdfjsLib.getDocument({ data: bytes }).promise;
    const paragraphs = [];

    for (let p = 1; p <= pdf.numPages; p++) {
      const content = await (await pdf.getPage(p)).getTextContent();
      let line = "";
      let lastY = null;
      let gap = 0;

      for (const item of content.items) {
        const y = item.transform[5];
        const height = item.height || 12;
        if (lastY !== null && Math.abs(y - lastY) > 1) {
          gap = Math.abs(y - lastY);
          if (gap > height * 1.5) {
            if (line.trim()) paragraphs.push(line.trim());
            line = "";
          } else {
            line += " ";
          }
        }
        line += item.str;
        lastY = y;
      }
      if (line.trim()) paragraphs.push(line.trim());
    }

    return paragraphs.map((t) => "<p>" + this.escapeHtml(t) + "</p>").join("");
  },

  escapeHtml: function (text) {
    const el = document.createElement("div");
    el.textContent = text;
    return el.innerHTML;
  },

  // The upload's multipart part carries no content type, so the server resolves the type from
  // the FILE NAME's extension — a synthesised name without one is rejected as unsupported.
  IMAGE_EXTENSIONS: {
    "image/png": ".png",
    "image/jpeg": ".jpg",
    "image/gif": ".gif",
    "image/webp": ".webp",
  },

  // Every data-URI image becomes a backend reference, using the same upload as the toolbar.
  // An image that fails to upload is dropped rather than left as base64, because base64 in a
  // stored template is the thing ZD-82 exists to prevent.
  uploadEmbeddedImages: async function (dotNet, html) {
    const doc = new DOMParser().parseFromString(html, "text/html");
    const images = [...doc.querySelectorAll("img")];

    for (let i = 0; i < images.length; i++) {
      const img = images[i];
      const src = img.getAttribute("src") || "";
      const match = /^data:([^;]+);base64,(.*)$/i.exec(src);
      if (!match) continue;

      const contentType = match[1].toLowerCase();
      const name = "imported-" + (i + 1) + (this.IMAGE_EXTENSIONS[contentType] || "");
      const url = await this.upload(dotNet, name, contentType, match[2]);
      if (url) img.setAttribute("src", url);
      else img.remove();
    }

    return doc.body.innerHTML;
  },
};
