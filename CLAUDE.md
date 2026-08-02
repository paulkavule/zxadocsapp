# CLAUDE.md

Guidance for Claude Code when making edits in this repository. Derived from `AGENTS.md` — read both before non-trivial work.

## What this repo is

ZXADOCS document workflow application. The runtime app is a Blazor Server UI (`zxadocsui`) backed by a shared services/DTO project (`zxadocsfe`). There is also an unused Blazor WebAssembly project (`zxadocsapp.Client`) on disk that is NOT in `DocsApp.sln` — treat it as secondary unless the task explicitly targets it.

## Project layout

- `DocsApp.sln` — includes `zxadocsui` and `zxadocsfe` only.
- `zxadocsui/` — main app. ASP.NET Core / Blazor Server (interactive server components). Uses MudBlazor, Tailwind CSS, custom JS, PDF.js.
- `zxadocsfe/` — shared services + DTOs. HTTP helpers, auth service, constants, DTOs. Depends on the external package `pkavule.zxadocslib`.
- `zxadocsapp.Client/` — secondary, out-of-solution WASM client.

Target frameworks (do NOT bump without explicit instruction):
- `zxadocsui` → `net10.0`
- `zxadocsfe` → `net10.0`
- `zxadocsapp.Client` → `net8.0`

## Default edit target

- Edit `zxadocsui` unless the task is clearly about shared services, DTOs, or HTTP plumbing — then edit `zxadocsfe`.
- Do NOT edit `zxadocsapp.Client` unless explicitly asked.
- Preserve existing naming (including misspellings like `HttpIntercetpor.cs`) unless the task is a cleanup.

## Key entry points

- App entry: `zxadocsui/Program.cs`
- Root shell: `zxadocsui/Components/App.razor`
- Router: `zxadocsui/Components/Routes.razor`
- HTTP layer: `zxadocsfe/Services/HttpService.cs`
- Auth: `zxadocsfe/Services/AuthenticationService.cs`
- Auth interceptor: `zxadocsui/Infrastructure/Http/HttpIntercetpor.cs`
- Login flow: `zxadocsui/Components/Pages/Login.razor.cs`

Runtime flow: `/` → `Login.razor` authenticates → token stored in local storage + app state → dashboard pages call backend via `IHttpService`.

## Real routes (verify before linking)

`/`, `/dashboard`, `/documents`, `/docfolders`, `/workflows`, `/createdocument`, `/viewdocument/{DocId?}`, `/not-found`.

Some links in the code still point to non-existent routes (e.g. `/newdocument`, `/new-documents`). Do not infer routes from button text — grep `@page` directives.

## Service lifetimes (DO NOT change to singleton)

These MUST stay scoped (server-side Blazor — singleton would leak state across users):

- `IHttpService`, `IAuthService`, `IUserSession`
- `SideDialogService`, `AppState`, `RequestContext`

If a change request implies moving any of these to singleton, push back and explain the cross-user leak risk before proceeding.

## Known risks / landmines

- Hardcoded default credentials in `Login.razor.cs` — do not propagate; flag if relevant.
- `GenerateNewCoreToken` (refresh token path) is a stub.
- API base URL is hardcoded in `Program.cs` as `https://localhost:7028/`.
- Several pages hardcode `userName`, `userId`, `organisationId` — prefer reading from `IUserSession` if you're already in that file.
- No test project in this repo — the templates/drafting suite lives in `../DocsApi/zxadocsapi.Tests`. Verify by building, running that suite, and running the app.
- The worktree may already have unrelated user changes — never revert them.

## UI notes

- MudBlazor is the primary component library — prefer its components over hand-rolled HTML.
- Tailwind output lives at `zxadocsui/wwwroot/ctailwindcss.css` (committed). There is no checked-in npm script for regenerating it — inspect `package.json` and `tailwind.config.js` first if a rebuild is needed.
- Custom JS / PDF.js under `zxadocsui/wwwroot/js/` and `zxadocsui/wwwroot/pdfjs/` — if a UI bug looks isolated to a component but isn't, check these too.

## Document workflow surfaces

When touching document flows, inspect together:

- `zxadocsui/Components/Pages/Dashboard/Documents*`
- `zxadocsui/Components/Pages/Dashboard/CreateWorkflow*`
- `zxadocsui/Components/Pages/Dashboard/CreateDocumentWorkflow*`
- `zxadocsui/Components/Pages/Dashboard/ViewWorkflow*`
- `zxadocsui/Components/Pages/Dashboard/EditDocWorkflow.razor`
- `zxadocsui/Components/DocWorkflow/*`

When touching auth or HTTP, inspect `Login.razor.cs` and `HttpIntercetpor.cs` together.

## Importing Word/PDF into the editor (ZD-85)

Conversion runs **in the browser**, and the API is asked for nothing new:

- **DOCX** → `mammoth`, vendored at `wwwroot/lib/mammoth/mammoth.browser.min.js` and lazy-loaded on
  first import (620KB — do not add it to `App.razor`).
- **PDF** → the `pdfjsLib` already loaded globally in `App.razor`. Text only: paragraphs are rebuilt
  from baseline gaps, and tables and columns cannot be recovered. Do not claim otherwise.
- **Images** → the same upload the toolbar's image button uses, so a stored template holds `?id=`
  references and never base64. An image that fails to upload is dropped, not left as a data URI.
- **Sanitisation** → none is written. Quill's clipboard is the allowlist: it keeps only what it has
  blots for, and what gets stored is the editor's own export rather than the converter's output.

`zxQuill.pickAndImport` is a thin wrapper over `zxQuill.importDocument(el, fileName, base64)` purely
so the browser tests can drive an import — a native file dialog cannot be automated. Fixtures for
both formats live in `zxadocsui.Tests/fixtures/`.

## Templates and drafting — render checks are part of the task

Rendering in this module breaks **silently**: the editor keeps working while a preview or the
generated PDF quietly moves an image or reflows a table, with no error and nothing in the log. A
successful build proves nothing here. Whenever you edit any of

- `Components/Custom/RichTextEditor.razor(.css)`, `Components/Custom/PagePreview.razor(.css)`
- `wwwroot/js/quilleditor.js`, `wwwroot/js/pagepreview.js`, the import path or its fixtures
- `Components/Pages/Dashboard/Templates/*`, `Components/Pages/Dashboard/Drafts/*`
- `PageGeometry` or `DocumentHtml` in `pkavule.zxadocslib`

do both of the following before reporting the change as done.

**Run the checks** — one script, three tiers:

```bash
./scripts/verify-render.sh                 # fast + ui + pdf
./scripts/verify-render.sh fast            # no browser, no server (~10s)
./scripts/verify-render.sh ui pdf --start  # launches the API and UI first
```

The `ui` tier needs an **approver account** in the environment, or the preview-surface test stops
after the first surface and skips the rest:

```bash
export ZXADOCS_APPROVER=<user holding ApproveTemplate/ApproveDraft>
export ZXADOCS_APPROVER_PASSWORD=<their password>
```

The app's dev-default login holds ViewTemplates/CreateTemplate/ViewDrafts/CreateDraft and **neither**
approve permission, so nothing it authors can reach the approved state the draft screens require. The
credentials stay in the environment because the test files are committed.

- `fast` — build, shared-contract + culture + stored-document + normalizer assertions, the
  signer-lifetime guard, and the dead-style guards (`zxadocslib.Tests`,
  `../DocsApi/zxadocsapi.Tests` with `Category!=Pdf`, `zxadocsui.Tests` with `Category=Fast`)
- `ui` — the editor and all four preview surfaces in a real browser plus the save-and-reopen round
  trip (`zxadocsui.Tests`, `Category=Ui`, Playwright driving the installed Chrome)
- `pdf` — a real LibreOffice conversion, measured (`../DocsApi/zxadocsapi.Tests`, `Category=Pdf`)

The `fast` tier runs automatically after any edit to the module via the `PostToolUse` hook in
`../.claude/settings.json`. It is debounced, does not block on a build failure (mid-change code
often does not compile), and does block on a failing assertion. It never runs `ui` or `pdf` — those
need a running app and LibreOffice, so run them before calling a change done.

**Restart the app by port, not by name.** `dotnet run` launches the apphost binary
`bin/Debug/net10.0/zxadocsui`, so `pkill -f zxadocsui.dll` and `pkill -f "dotnet run"` both miss it
and the OLD process keeps the port. The browser tier then measures the previous build and reports a
fix as broken — or worse, as working. Use `lsof -ti :7086 -sTCP:LISTEN | xargs kill` (and `:7028` for
the API), then confirm the page's `zxadocsui.<hash>.styles.css` fingerprint matches
`obj/Debug/net10.0/staticwebassets.build.endpoints.json`; a mismatch means you are still on the old
process. Deleting `obj/` under a running process also makes it serve an **empty** gzip stylesheet, so
every scoped CSS rule silently disappears.

**What the browser pass asserts:**

| Where | Expected |
| --- | --- |
| Editor sheet | `794 x 1123`, `box-sizing: border-box`, `.ql-editor` padding `0`, text column `605px` |
| Editor image | `naturalWidth > 0` (0 means the URL failed, not CSS), width `<= 605`, aspect preserved |
| Editor image, clicked | `.ql-resize-overlay` rect equals the image rect, so handles drag |
| Editor image, centred | applies `ql-resize-style-center`, and that class exists in the stored document CSS too |
| Export | every `<img>` has **both** `width` and `height`; the first table row has `width="N%"` per cell |
| Save, then "New version" | the table comes back with its rows, cells and text; the image still loads |
| All four previews | sheet aspect `0.707`, fits its pane, centred, and the document declares `size:210mm 297mm`. One test walks template detail → approvals queue → draft editor → draft detail on a single template, because each state is produced by acting on the previous one (submit, then approve as the second actor) |
| Template approvals | Approve and Reject are within the viewport — a page-shaped preview with no height bound grew to ~2120px and pushed them off screen |
| Word import | headings, 3 bullets, a bold run, the 3x2 table with 6 cells, and an image whose `src` is `?id=…` with **no** `data:` — then the same after a save and reopen |
| PDF import | the words arrive as multiple paragraphs (text only; a PDF has no structure to recover) |
| Generated PDF | page `595.3 x 841.9 pt`; image inside the margins (right edge `<= 524.4 pt`), ~100% of the 453.5 pt column, native aspect; table column ratios within ~0.1pp of the browser |

A PDF needs no special tooling to check: `/MediaBox` gives the page, and the `cm ... Do` operator
in an inflated content stream gives each image's drawn width, height and x-position.

### Do not regress these (each was found the hard way; the wrong form fails silently)

- `TemplateImageUrlSigner` stays **Singleton**. Scoped gives each request its own random key when
  none is configured, so no signed image URL ever validates and every image renders broken.
- Load saved content with `updateContents` onto an emptied document, never `setContents` — the
  latter does not rebuild the table plugin's blots (same Delta: 0 rows vs a full table).
- The same applies to HTML: load it via `zxQuill.setHtml`, which converts to a Delta and applies
  it, and **never** call `clipboard.dangerouslyPasteHTML` — measured on one imported document, it
  produced a table with **0 rows** where convert + `updateContents` produced all 3 rows and 6
  cells. `dangerouslyPasteHTML` is `setContents` underneath, so it loses the same blots.
- An upload's multipart part carries **no content type** (`HttpService` does not set one), so the
  server resolves the type from the FILE NAME's extension. A synthesised name must carry one —
  `imported-1` is rejected as unsupported, `imported-1.png` is accepted.
- Tables round-trip via the stored **Delta**, not HTML; the table plugin's HTML-to-Delta path gives
  each row a different table identity so rows never re-assemble.
- Column widths go on the **first row's cells** as percentages; a percentage `<colgroup>` is only
  partly honoured and overrides the cells.
- `@page` needs explicit `size:210mm 297mm`; the `size:A4` keyword is ignored and the page silently
  becomes the render host's default.
- `PagePreview` must size itself via `aspect-ratio`: Blazor CSS isolation keeps a calling page's
  rules off it, and arbitrary Tailwind classes like `h-[560px]` are absent from the committed
  `ctailwindcss.css`, so a host with no height collapses and the preview vanishes.
- To BOUND a preview from a page, wrap it in an element that page owns and pass the **global**
  `h-full` — scoped CSS cannot reach the component's root, and `min-h-0` is not in the committed
  Tailwind build either. Unbounded, the preview is 1.41x its own width and pushes whatever follows it
  off screen (`TemplateApprovals.razor.css`).
- Responsive variants are as dead as arbitrary values when absent from the committed CSS:
  `lg:grid-cols-2` was never generated, so both approvals pages have always been a single full-width
  column. `TailwindDeadClassTests` now checks both shapes.
- Never format a CSS length from `PageGeometry`'s millimetre members — they are doubles and format
  with `CurrentCulture`, so a comma-decimal host emits `2,5cm` and the browser drops the declaration.
  Consume `PageGeometry.MarginCss`; `CultureSafeGeometryTests` enforces it.
- Signed image URLs must be canonicalised inside the stored **Delta** as well as the visible markup —
  the Delta is base64 in a comment, so a text pass cannot see it. Left there, every version pins an
  expiring signature and the content hash never matches, so each re-save spawns a redundant version.
- Keep `body{margin:0}` with the margin on `@page` — a body margin collapses every table column.
- `<table border="1">` and `<img width height>` together are required; LibreOffice ignores the CSS
  equivalents.
- Any surface rendering stored HTML must sign its image URLs via
  `TemplateHtml.WithDisplayableImagesAsync`, or its images render broken.

## Build

`GH_PACKAGES_TOKEN` must be exported first, or nothing restores — see below.

From repo root:

```bash
export GH_PACKAGES_TOKEN=<GitHub PAT with read:packages>
dotnet build DocsApp.sln
# only if the task targets the WASM client:
dotnet build zxadocsapp.Client/zxadocsapp.Client.csproj
```

### pkavule.zxadocslib comes from GitHub Packages (ZD-30)

The shared contract is a `PackageReference`, not a project reference into the sibling `zxadocslib` repo — so editing that checkout no longer affects this build. Both `zxadocsfe` and `zxadocsui.Tests` reference it, so a version bump touches both. `nuget.config` at the repo root declares the feed and reads the token from the environment.

GitHub Packages requires authentication to restore *even a public package*; there is no anonymous access. Missing token means NuGet sends the literal `%GH_PACKAGES_TOKEN%` and restore dies with `401` / `NU1301`.

- **A Dock-launched IDE does not inherit `~/.zshrc` exports on macOS** — the same build passes in a terminal and fails in the IDE.
- **A Docker build sees neither the shell env nor `$HOME`**; its restore layer needs the token as a build secret.
- `nuget.config` has **no `<clear />`** on purpose — clearing inherited sources breaks MudBlazor and every other package.
- Never commit a literal token; the env-var placeholder is the committed form.

To change the shared contract: edit `zxadocslib`, bump `<Version>`, `dotnet pack`, push to the feed, bump the `PackageReference` in both projects here.

No test suite in this repo; for the templates/drafting module run `cd ../DocsApi && dotnet test zxadocsapi.Tests/zxadocsapi.Tests.csproj` and complete the browser pass above. Otherwise `dotnet build` is the primary correctness signal. For UI changes, say so explicitly if you cannot verify behavior in a browser rather than claiming success.

## Before any architecture-level change

Verify, in order:

1. Is this the actual runtime path (`zxadocsui`), or the unused WASM client?
2. Is the route name real, or referenced only by a stale link?
3. Is the relevant state scoped per user, or shared?
4. Is the backend contract defined locally, or in `pkavule.zxadocslib`?

If any answer is unclear, read the code first, then edit.
