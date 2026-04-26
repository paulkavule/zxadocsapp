# RISKS.md

## Scope

This file lists security and robustness risks visible from the current codebase. It is based on source inspection only, not a full runtime penetration test.

## High Severity

### 1. Hardcoded login credentials in source

File:

- `zxadocsui/Components/Pages/Login.razor.cs`

Observed:

- `_username` and `_password` are initialized with concrete values in code.

Why it matters:

- Credentials in source can leak through commits, screenshots, logs, local builds, or reused environments.
- Even if these are only test credentials, they normalize unsafe handling of secrets and increase accidental exposure risk.

Recommended action:

- Remove hardcoded values immediately.
- Use empty defaults in the UI.
- If seeded credentials are needed for local development, load them from a non-committed local configuration source.

### 2. Tokens stored in browser local storage

Files:

- `zxadocsui/Components/Pages/Login.razor.cs`
- `zxadocsui/wwwroot/js/dashboard-layout.js`

Observed:

- Auth token and refresh token are written to `localStorage`.

Why it matters:

- Any successful XSS in the app or in loaded third-party scripts can read and exfiltrate these tokens.
- This is a common token theft path for browser apps.

Recommended action:

- Prefer secure, `HttpOnly`, `Secure`, `SameSite` cookies for session handling where possible.
- If token storage must remain client-side, reduce token lifetime and harden the app against script injection.

### 3. Refresh token flow is incomplete and potentially unsafe

Files:

- `zxadocsfe/Services/AuthenticationService.cs`
- `zxadocsui/Infrastructure/Http/HttpIntercetpor.cs`

Observed:

- `GenerateNewCoreToken` returns a placeholder token object instead of performing a real refresh operation.
- The interceptor retries unauthorized requests using that result.

Why it matters:

- This creates undefined auth behavior and may hide real authentication failures.
- If later completed carelessly, refresh logic in a delegating handler can easily produce replay loops, token confusion, or silent auth bypass assumptions.

Recommended action:

- Implement a real refresh endpoint call.
- Distinguish access-token refresh failure from request failure.
- Clear local auth state and force re-authentication when refresh fails.

## Medium Severity

### 4. Refresh token is stored incorrectly in session state

File:

- `zxadocsui/Components/Pages/Login.razor.cs`

Observed:

- `session.AddItem("refreshToken", token.Token)` stores the access token under the refresh token key.

Why it matters:

- This breaks token semantics and can create confusing auth behavior.
- Security fixes built later on top of incorrect state handling are likely to fail in subtle ways.

Recommended action:

- Store `token.RefereshToken` under the refresh-token key.
- Standardize naming for `refresh_token`, `refreshToken`, and `RefereshToken`.

### 5. Hardcoded backend base URL

File:

- `zxadocsui/Program.cs`

Observed:

- The named API client uses `https://localhost:7028/` directly in code.

Why it matters:

- Environment-specific values in code increase misconfiguration risk.
- Production deployments may accidentally target the wrong backend or force unsafe ad hoc edits.

Recommended action:

- Move the API base URL to configuration.
- Validate environment-specific configuration at startup.

### 6. Stale and broken routes

Files:

- `zxadocsui/Components/Pages/Dashboard/Documents.razor.cs`
- `zxadocsui/Components/Layout/DashboardLayout.razor`
- `zxadocsui/Components/Pages/Dashboard/CreateWorkflow.razor.cs`
- `zxadocsui/Components/Pages/Dashboard/ViewWorkflow.razor.cs`

Observed:

- Code navigates to `/newdocument` and `/new-documents`, but the declared route is `/createdocument`.

Why it matters:

- Broken navigation can strand users in incomplete workflow states.
- In auth-sensitive flows, routing mismatches often cause unsafe fallback behavior, repeated submissions, or confusing session handling.

Recommended action:

- Centralize route constants or use typed navigation helpers.
- Remove stale paths and make the route map consistent.

### 7. Trusting JWT claims client-side without visible server-side session controls

Files:

- `zxadocsui/Components/Pages/Login.razor.cs`
- `zxadocsui/State/AppState.cs`

Observed:

- JWT claims are parsed on the client and copied into request context for later use.

Why it matters:

- Client-side claim parsing is fine for UI hints, but it must not become the source of truth for authorization decisions.
- If later code trusts `RequestContext.Claims` for sensitive actions, a compromised client context can drive incorrect behavior.

Recommended action:

- Treat client-side claims as display data only.
- Enforce all authorization decisions on the backend.

## Low Severity

### 8. External third-party scripts loaded from CDNs

Files:

- `zxadocsui/Components/App.razor`

Observed:

- Font Awesome and PDF.js are loaded from external CDNs.

Why it matters:

- CDN dependencies widen the supply-chain and runtime trust surface.
- If integrity and CSP are not enforced, compromised third-party assets can execute in the app origin context.

Recommended action:

- Prefer pinned local assets where feasible.
- If CDN use remains, add SRI where supported and define a restrictive CSP.

### 9. Console and debug output around auth and API operations

Files:

- `zxadocsfe/Services/HttpService.cs`
- `zxadocsui/Components/Pages/Login.razor.cs`
- `zxadocsui/Infrastructure/Http/HttpIntercetpor.cs`

Observed:

- The code logs API responses and auth-related events to console or app logs.

Why it matters:

- Depending on payload shape, logs may eventually expose sensitive content.
- Debug logging tends to persist longer than intended.

Recommended action:

- Remove debug prints that expose response bodies or auth flow details.
- Use structured logging with explicit redaction rules.

### 10. Input and identity values are still partially hardcoded in workflow pages

Files:

- `zxadocsui/Components/Pages/Dashboard/Documents.razor.cs`
- `zxadocsui/Components/Pages/Dashboard/CreateWorkflow.razor.cs`
- `zxadocsui/Components/Pages/Dashboard/ViewWorkflow.razor.cs`

Observed:

- Several pages still use hardcoded `userName`, `userId`, or `organisationId` values.

Why it matters:

- Hardcoded identifiers can expose the wrong data set, bypass intended tenant scoping, or cause accidental data crossover during testing and demos.

Recommended action:

- Source all user and tenant identifiers from validated authenticated context.
- Remove hardcoded identity values from page logic.

## Risk That Was Reduced

### Scoped per-user state

File:

- `zxadocsui/Program.cs`

Observed:

- `SideDialogService`, `AppState`, and `RequestContext` are now registered as scoped services.

Why it matters:

- This reduces the earlier risk of cross-user state leakage that would have existed with singleton lifetimes in a server-side Blazor app.

Remaining caution:

- Keep these services scoped unless there is a strong, reviewed reason not to.
