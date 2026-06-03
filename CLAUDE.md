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
- No test projects exist — verify behavior by building and, where possible, running the app.
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

## Build

From repo root:

```bash
dotnet build DocsApp.sln
# only if the task targets the WASM client:
dotnet build zxadocsapp.Client/zxadocsapp.Client.csproj
```

No test suite — `dotnet build` is the primary correctness signal. For UI changes, say so explicitly if you cannot verify behavior in a browser rather than claiming success.

## Before any architecture-level change

Verify, in order:

1. Is this the actual runtime path (`zxadocsui`), or the unused WASM client?
2. Is the route name real, or referenced only by a stale link?
3. Is the relevant state scoped per user, or shared?
4. Is the backend contract defined locally, or in `pkavule.zxadocslib`?

If any answer is unclear, read the code first, then edit.
