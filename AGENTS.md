# AGENTS.md

## Purpose

This repository contains the current ZXADOCS document workflow UI work. The primary application is a Blazor Server app in `zxadocsui` backed by a shared service/library project in `zxadocsfe`.

This file is for coding agents working in this repo. It is intended to reduce wrong assumptions before making changes.

## Repo Layout

- `DocsApp.sln`
  - Solution file. Currently includes `zxadocsui` and `zxadocsfe`.
- `zxadocsui/`
  - Main application.
  - ASP.NET Core / Blazor Server using interactive server components.
  - Uses MudBlazor, Tailwind-generated CSS, custom JS, and PDF.js integration.
- `zxadocsfe/`
  - Shared service and DTO layer used by the UI.
  - Contains HTTP helpers, auth service, constants, and DTOs.
- `zxadocsapp.Client/`
  - Separate Blazor WebAssembly client project on disk.
  - Not currently included in `DocsApp.sln`.
  - Treat it as secondary unless the task explicitly targets it.

## Current Architecture

- Entry point for the main app: `zxadocsui/Program.cs`
- Root component shell: `zxadocsui/Components/App.razor`
- Router: `zxadocsui/Components/Routes.razor`
- Shared HTTP layer: `zxadocsfe/Services/HttpService.cs`
- Auth wrapper: `zxadocsfe/Services/AuthenticationService.cs`
- HTTP auth interceptor: `zxadocsui/Infrastructure/Http/HttpIntercetpor.cs`

The main runtime flow is:

1. User lands on `/`
2. `Login.razor` authenticates against the backend
3. JWT/token data is stored in local storage and copied into app state
4. Pages in the dashboard call the backend through `IHttpService`

## Main Routes

These are the concrete routes currently declared in Razor pages:

- `/` -> login
- `/dashboard`
- `/documents`
- `/docfolders`
- `/workflows`
- `/createdocument`
- `/viewdocument/{DocId?}`
- `/not-found`

Do not assume route names from button text. Some links in the codebase still point to paths that do not exist, such as `/newdocument` and `/new-documents`.

## Framework and Dependency Notes

- `zxadocsui` targets `net10.0`
- `zxadocsfe` targets `net10.0`
- `zxadocsapp.Client` targets `net8.0`
- `zxadocsui` references `zxadocsfe`
- `zxadocsfe` depends on the external package `pkavule.zxadocslib`
- `zxadocsui/package.json` only provides Tailwind CLI dependencies; there are no checked-in npm scripts

Be careful when changing package versions or target frameworks. This repo already spans mixed .NET targets.

## State and Lifetime Warnings

The current service lifetimes are important:

- `IHttpService` is scoped
- `IAuthService` is scoped
- `IUserSession` is scoped
- `SideDialogService` is scoped
- `AppState` is scoped
- `RequestContext` is scoped

This is the correct direction for a server-side Blazor app. If a future change proposes moving these back to singleton, treat that as a potential cross-user state leak and review very carefully.

## Known Codebase Risks

Agents should account for these before making changes:

- The earlier cross-user leakage risk from singleton per-user state has been reduced by moving `SideDialogService`, `AppState`, and `RequestContext` to scoped lifetime
- Login currently contains hardcoded default credentials in `zxadocsui/Components/Pages/Login.razor.cs`
- Refresh token handling is incomplete; `GenerateNewCoreToken` is effectively a stub
- API base URL is hardcoded in `zxadocsui/Program.cs` as `https://localhost:7028/`
- Several pages still hardcode user values such as `userName`, `userId`, or `organisationId`
- The worktree may already be dirty; do not revert unrelated user changes
- The only test project is `zxadocsui.Tests` (render checks for the templates/drafting module);
  everything else is covered from `../DocsApi/zxadocsapi.Tests` and `../zxadocslib/zxadocslib.Tests`

## UI Notes

- MudBlazor is the primary component library
- Tailwind output is committed under `zxadocsui/wwwroot/ctailwindcss.css`
- Dashboard behavior also depends on custom scripts in:
  - `zxadocsui/wwwroot/js/`
  - `zxadocsui/wwwroot/pdfjs/`

If a UI change appears broken, check both Razor and the corresponding JS/CSS assets before concluding the component is isolated.

## Practical Build Targets

Use these commands from the repo root:

```bash
dotnet build DocsApp.sln
dotnet build zxadocsapp.Client/zxadocsapp.Client.csproj
```

If Tailwind output must be regenerated, inspect `zxadocsui/package.json`, `zxadocsui/tailwind.config.js`, and the CSS files under `zxadocsui/wwwroot/` first. There is no authoritative npm script checked in for that workflow.

## Editing Guidance

- Prefer changing `zxadocsui` unless the task is clearly about shared services or DTOs
- If you touch auth or request handling, inspect both `Login.razor.cs` and `HttpCoreIntercetpor.cs`
- If you touch document flows, inspect:
  - `Components/Pages/Dashboard/Documents*`
  - `Components/Pages/Dashboard/CreateWorkflow*`
  - `Components/Pages/Dashboard/CreateDocumentWorkflow*`
  - `Components/Pages/Dashboard/ViewWorkflow*`
  - `Components/DocWorkflow/*`
- If you touch the templates or drafting module, the render checks in
  "Templates and Drafting: Mandatory Render Checks" are part of the task, not a follow-up
- Preserve current naming where feasible, even when names are misspelled, unless the task includes cleanup

## Templates and Drafting: Mandatory Render Checks

Any change touching the Legal Templates / Contract Drafting module must be verified against the
checks below before it is called done. Rendering here breaks **silently**: the editor keeps
working while the preview or the generated PDF quietly moves an image or reflows a table, and
nothing fails or logs. Building is not evidence.

### When this applies

Trigger on an edit to any of:

- `zxadocsui/Components/Custom/RichTextEditor.razor` / `.razor.css`
- `zxadocsui/Components/Custom/PagePreview.razor` / `.razor.css`
- `zxadocsui/wwwroot/js/quilleditor.js`, `zxadocsui/wwwroot/js/pagepreview.js`
- `zxadocsui/Components/Pages/Dashboard/Templates/*`
- `zxadocsui/Components/Pages/Dashboard/Drafts/*`
- `PageGeometry` or `DocumentHtml` in `pkavule.zxadocslib` (the page and the stored-document
  contract; changing either moves every surface at once)

### How to run the checks

One command runs everything, cheapest tier first:

```bash
./scripts/verify-render.sh                 # fast + ui + pdf
./scripts/verify-render.sh fast            # no browser, no server (~10s)
./scripts/verify-render.sh ui pdf --start  # launches the API and UI first
```

The tiers:

| Tier | What it covers | Where it lives |
| --- | --- | --- |
| `fast` | Build; shared-contract and culture assertions; stored-document and normalizer assertions; the signer-lifetime guard; the dead-style guards | `zxadocslib.Tests`, `../DocsApi/zxadocsapi.Tests` (`Category!=Pdf`), `zxadocsui.Tests` (`Category=Fast`) |
| `ui` | The editor and all four preview surfaces in a real browser, plus the save-and-reopen round trip. Needs `ZXADOCS_APPROVER` / `ZXADOCS_APPROVER_PASSWORD` — see below | `zxadocsui.Tests` (`Category=Ui`), Playwright |
| `pdf` | A real LibreOffice conversion, measured | `../DocsApi/zxadocsapi.Tests` (`Category=Pdf`) |

The `fast` tier also runs automatically after any edit to the module, via the `PostToolUse` hook in
`../.claude/settings.json` (script: `../.claude/hooks/templates-render-guard.sh`). It is debounced,
reports a build failure without blocking, and blocks on a failing assertion. It does **not** run the
`ui` or `pdf` tiers — those need a running app and LibreOffice, so run them yourself before calling
a change done.

Playwright drives the installed Chrome (`Channel = "chrome"`); no browser download and no `pwsh`
needed. UI tests name every template they create with an `[e2e-…]` prefix and archive it afterwards.

The preview-surface test needs a **second actor**: the app's dev-default login holds no approve
permission, so a template it authors can never reach the approved state the draft screens require.
Export `ZXADOCS_APPROVER` and `ZXADOCS_APPROVER_PASSWORD` for a user holding
`ApproveTemplate`/`ApproveDraft`; without them the test asserts the first surface and skips the rest
rather than passing silently. The credentials are not committed.

**Restart the app by port.** `dotnet run` launches `bin/Debug/net10.0/zxadocsui`, so `pkill -f
zxadocsui.dll` and `pkill -f "dotnet run"` both miss it and the stale process keeps the port — the
browser tier then measures the previous build. Use `lsof -ti :7086 -sTCP:LISTEN | xargs kill`
(`:7028` for the API). Deleting `obj/` under a running process makes it serve an empty gzip
stylesheet, which silently removes every scoped CSS rule.

### What the browser pass asserts

| Surface | Check | Expected |
| --- | --- | --- |
| Editor | sheet box, `box-sizing`, editor padding | `794 x 1123`, `border-box`, `0` |
| Editor | text column | `605px` (= `PageGeometry.ContentWidthPx`) |
| Editor | image loads | `naturalWidth > 0` — a 0-width image means the URL failed, not a CSS problem |
| Editor | image width | `<= 605`, aspect preserved |
| Editor | click image | `.ql-resize-overlay` rect equals the image rect (handles are draggable) |
| Editor | align centre | applies `ql-resize-style-center`; the class must also exist in the stored document's CSS |
| Editor | export | every `<img>` carries **both** `width` and `height`; first table row carries `width="N%"` per cell |
| Save then reopen | New version on a saved template | table returns with its rows, cells and text; image still loads |
| Template detail / approvals / draft editor / draft detail previews | sheet aspect | `0.707`, fits its pane, centred, document declares `size:210mm 297mm`. One test walks all four on a single template: each state is produced by acting on the previous one |
| Template approvals | action bar | Approve and Reject within the viewport — unbounded, the preview grew to ~2120px and pushed them off screen |
| Word import | structure | headings, 3 bullets, a bold run, the 3x2 table with 6 cells |
| Word import | image | `src` is `?id=…` with no `data:`, and it still loads after a save and reopen |
| PDF import | text | the words arrive as multiple paragraphs (text only by nature) |
| Generated PDF | page | `595.3 x 841.9 pt` (210 x 297 mm) |
| Generated PDF | image | within the margins (right edge `<= 524.4 pt`), `~100%` of the 453.5 pt column, native aspect |
| Generated PDF | table | column ratios within ~0.1 percentage point of the browser's |

A PDF can be measured without any PDF tooling: `/MediaBox` gives the page, and the `cm ... Do`
operator in an inflated content stream gives each drawn image's width, height and x-position.

### Importing Word/PDF into the editor (ZD-85)

Conversion runs in the **browser**; the API gains nothing:

| Part | How |
| --- | --- |
| DOCX | `mammoth`, vendored at `wwwroot/lib/mammoth/mammoth.browser.min.js`, lazy-loaded on first import (620KB — deliberately not in `App.razor`) |
| PDF | the `pdfjsLib` already loaded globally; text only, paragraphs rebuilt from baseline gaps. Tables and columns are not recoverable |
| Images | the same upload the toolbar image button uses, so stored HTML holds `?id=` references and never base64; an image that fails to upload is dropped |
| Sanitisation | none written — Quill's clipboard keeps only what it has blots for, and storage takes the editor's export, not the converter's output |

`zxQuill.pickAndImport` wraps `zxQuill.importDocument(el, fileName, base64)` so the browser tests can
drive an import without a native file dialog. Fixtures live in `zxadocsui.Tests/fixtures/`.

### Landmines — verified behaviours that must not be regressed

Each cost real time to find; the wrong form fails silently.

- To BOUND a preview from a page, wrap it in an element that page owns and pass the **global**
  `h-full`. Scoped CSS cannot reach a child component's root, and `min-h-0` is absent from the
  committed Tailwind build, so neither can size it. Unbounded, `PagePreview` is 1.41x its own width.
- A responsive variant is as dead as an arbitrary value when the committed CSS lacks it:
  `lg:grid-cols-2` was never generated, so both approvals pages have always been one full-width
  column. `TailwindDeadClassTests` checks both shapes now.
- Never format a CSS length from `PageGeometry`'s millimetre members (doubles → `CurrentCulture` →
  `2,5cm` on a comma-decimal host, which the browser drops). Use `PageGeometry.MarginCss`;
  `CultureSafeGeometryTests` enforces it.
- Canonicalise signed image URLs inside the stored **Delta**, not just the visible markup — it is
  base64 in a comment, so a text pass cannot see it. Left there, every version pins an expiring
  signature and the SHA-256 dedup never matches, so each re-save spawns a redundant version.
- `TemplateImageUrlSigner` must stay **Singleton**. Scoped gives every request its own random key
  when no key is configured, so signed image URLs never validate and every image renders broken.
- Load a saved document into the editor with `updateContents` onto an emptied document, never
  `setContents` — `setContents` does not rebuild the table plugin's blots (same Delta: 0 rows vs a
  full table).
- HTML is loaded the same way: `zxQuill.setHtml` converts to a Delta and applies it. Never call
  `clipboard.dangerouslyPasteHTML` — measured on one imported document it gave a table with **0
  rows**, where convert + `updateContents` gave all 3 rows and 6 cells. It is `setContents`
  underneath, so it loses the same blots.
- An upload's multipart part carries **no content type**, so the server resolves the type from the
  file name's extension. A synthesised name needs one: `imported-1` is rejected as unsupported,
  `imported-1.png` is accepted.
- Tables must round-trip through the stored **Delta**, not HTML. The table plugin's HTML-to-Delta
  path assigns each row a different table identity, so rows never re-assemble.
- Column widths belong on the **first row's cells** as percentages. A percentage `<colgroup>` is
  only partly honoured by LibreOffice and overrides the cells.
- `@page` needs explicit dimensions (`size:210mm 297mm`). The `size:A4` keyword is ignored on
  import and the page silently becomes the render host's default.
- `PagePreview` must size itself (`aspect-ratio`). Blazor CSS isolation stops a calling page's
  rules from reaching it, and arbitrary Tailwind classes such as `h-[560px]` do not exist in the
  committed `ctailwindcss.css` — a host with no height collapses and the preview disappears.
- Keep `body{margin:0}` with the margin on `@page`; a body margin collapses every table column.
- `<table border="1">` and `<img width height>` (both attributes) are required — LibreOffice
  ignores the CSS equivalents.
- Any surface showing stored HTML must sign its image URLs
  (`TemplateHtml.WithDisplayableImagesAsync`), or its images render broken.

## Before Large Refactors

Before changing architecture-level behavior, verify all of the following:

- Is the target project the actual runtime path, or only the unused WebAssembly client?
- Is a route name real, or only referenced by a stale link?
- Is the state service scoped per user, or shared globally?
- Is the backend contract defined in local code, or only in `pkavule.zxadocslib`?

If any of those are unclear, inspect first and then change code.
