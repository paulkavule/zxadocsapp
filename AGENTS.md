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
- There are no test projects in the repository at the moment

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
- Preserve current naming where feasible, even when names are misspelled, unless the task includes cleanup

## Before Large Refactors

Before changing architecture-level behavior, verify all of the following:

- Is the target project the actual runtime path, or only the unused WebAssembly client?
- Is a route name real, or only referenced by a stale link?
- Is the state service scoped per user, or shared globally?
- Is the backend contract defined in local code, or only in `pkavule.zxadocslib`?

If any of those are unclear, inspect first and then change code.
