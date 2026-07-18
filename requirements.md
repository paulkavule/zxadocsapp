# ZXADOCS — Requirements Specification

**Product:** ZXADOCS Document Workflow Application
**Document type:** Reverse-engineered requirements (user stories + acceptance criteria)
**Source:** Derived by scanning the codebase (`zxadocsui` Blazor Server app + `zxadocsfe` shared services/DTOs).
**Status:** Draft — reflects behaviour observed in code as of the current branch. Items marked _(inferred)_ are deduced from code and should be confirmed with product owners. Items marked _(gap)_ appear to be scaffolded/incomplete in the current implementation.

---

## 1. Overview

ZXADOCS is a document-approval and workflow application for organisations. Authenticated users upload documents, route them through an ordered chain of approvers ("actors"), and each approver reviews, annotates (signature / comment), and either approves-and-forwards or rejects the document. Documents move through folder-like states (Draft, Outbox, Published, Archived, Deleted). The system supports multiple roles per user, per-organisation scoping (multi-tenancy), user management, and reporting/audit surfaces.

### 1.1 Architecture context (for reference only)

- **UI:** `zxadocsui` — ASP.NET Core / Blazor Server (interactive server components), MudBlazor, Tailwind CSS, PDF.js for document rendering.
- **Shared services/DTOs:** `zxadocsfe` — HTTP service, authentication service, DTOs; depends on external package `pkavule.zxadocslib`.
- **Backend:** A remote REST API (base URL hardcoded as `https://localhost:7028/`) accessed via `IHttpService`. Endpoints observed: `api/user/login`, `api/user`, `api/users`, `api/document(s)`, `api/upload`, `api/documents/sign/{id}/{nextActor}`, `api/docworkflow/{docId}`, `api/doccategoryworkflow`, `api/doccategory`, `api/listoptions`, `api/documents/dashboard`.
- **Auth:** JWT bearer tokens stored in browser local storage and app state; an HTTP interceptor attaches the token to outgoing requests.

---

## 2. Actors / Roles

| Actor | Description |
|---|---|
| **Anonymous visitor** | Unauthenticated user at the login screen. |
| **Authenticated user** | Any logged-in user with a valid JWT and at least one role. |
| **Document originator** | User who uploads/creates a document and starts a workflow. |
| **Approver / Actor** | A user assigned to a workflow level who must review and sign or reject a document when it becomes their turn (`NextActor`). |
| **Administrator** | User with rights to manage users, roles, workflow templates, and system settings. |
| **Organisation** | Tenant boundary; users, roles, documents, workflow templates, and departments are scoped to an `OrganisationId` / `EntityId`. |

> **Baseline role taxonomy** (`UserRoles` enum in `pkavule.zxadocslib`): **User**, **Admin**, **Approver**, **Filler**. Roles are otherwise data-driven (loaded from `api/listoptions?type=role`); a user may hold multiple roles and selects one at login when more than one exists.
>
> **Authorization note:** There is **no role-based route guarding** in the Blazor router — any page is reachable by URL if authenticated. Effective access control is **data-driven** (a document is only actionable by its current `NextActor`) and enforced by the backend via the bearer token. Adding route/role authorization is a recommended requirement (see NFR-1.7).

---

## 3. Functional Requirements

Organised as **Epics → User Stories → Acceptance Criteria**.

---

### EPIC 1 — Authentication & Session

#### US-1.1 — Log in with username and password
**As an** authenticated user, **I want to** sign in with my username and password **so that** I can access my document workspace.

**Acceptance criteria**
- **Given** I am on the login page (`/`), **when** I submit valid credentials, **then** I am authenticated and redirected to the dashboard (`/dashboard`).
- **Given** I submit invalid credentials, **when** the backend rejects them, **then** I see an error message "Invalid username or password" and remain on the login page.
- The login form must validate that username and password are provided before the submit is enabled/accepted.
- On success, the JWT access token and refresh token are stored, and user identity (user id, name, org, role) is decoded from the JWT and held in session.
- **Constraint / landmine:** the code currently ships **hardcoded default credentials** in `Login.razor.cs` — these must be removed before any non-dev release _(security requirement, see NFR)_.

#### US-1.2 — Select a role at login when I have more than one
**As a** multi-role user, **I want to** choose which role to use for this session **so that** the app shows the correct capabilities.

**Acceptance criteria**
- **Given** my account has exactly one role, **when** I log in, **then** that role is selected automatically with no prompt.
- **Given** my account has more than one role, **when** I log in, **then** I am presented with a role picker and must select one to continue.
- The selected role is stored in the session and drives subsequent authorization/context.

#### US-1.3 — Restore a persisted session
**As a** returning user, **I want** my session to persist **so that** I don't have to re-enter credentials every visit.

**Acceptance criteria**
- On successful login, the current-user session (including encrypted token values) is saved to protected browser storage.
- On a later visit, session data can be restored into the scoped user session when it is requested by a page.
- A "Remember" switch is visible on the login form, but it is currently **not connected to different persistence behaviour**. Whether persistence should be optional remains a product decision _(gap)_.
- **Implementation note:** the login page also reads legacy raw `localStorage` keys (`token`, `refresh_token`) into `AppState`; the current login flow does not write those keys. This legacy path should be consolidated or removed _(gap)_.

#### US-1.4 — Authenticated requests carry my token
**As a** logged-in user, **I want** all my API calls to be authenticated automatically **so that** I can access protected resources.

**Acceptance criteria**
- Every outgoing API request includes the bearer token via the HTTP interceptor.
- **Given** my token is missing or rejected, **then** the app handles the unauthorized response gracefully (e.g. redirect to login) _(inferred — confirm behaviour)_.
- **Note / gap:** the refresh-token renewal path (`GenerateNewCoreToken`) is currently a **stub** and does not yet issue new tokens.

#### US-1.5 — Log out
**As a** logged-in user, **I want to** sign out **so that** my session is cleared on a shared machine.

**Acceptance criteria**
- **When** I log out, **then** protected session storage and in-memory session state are cleared and I am returned to the login page.
- **Gap:** logout does not currently remove the legacy raw `localStorage` keys read by the login page. If those keys remain supported, logout must clear them too.

---

### EPIC 2 — Dashboard & Navigation

#### US-2.1 — See a dashboard overview after login
**As an** authenticated user, **I want** a dashboard landing page **so that** I can see my document activity at a glance.

**Acceptance criteria**
- **Given** I am authenticated, **when** I navigate to `/dashboard`, **then** the dashboard renders within the shared dashboard layout.
- The dashboard shows KPI cards from `/statistics/summary` (**Pending**, **In Review**, **Completed**, **Average Resolution** time), all scoped to my organisation.
- A **weekly activity bar chart** is shown from `/statistics/weekly/summary?week={ISO week}`.
- A **recent activity** list is shown from `/statistics/recent-activity`.
- Dashboard content reflects the current user's context (name, org, role) drawn from session rather than hardcoded values _(current code hardcodes some identity values — see landmine)_.
- **Implementation note:** the dashboard shell also contains several hardcoded workflow rows, pending-approval entries, counts, and calls-to-action. These are presentation placeholders, not data-backed dashboard requirements _(gap)_.

#### US-2.2 — Navigate the app via the side menu
**As an** authenticated user, **I want** a consistent navigation menu **so that** I can reach documents, workflows, users, reports, and settings.

**Acceptance criteria**
- The dashboard layout exposes navigation to: Dashboard (`/dashboard`), Documents (`/documents`), Workflows (`/workflows`), Users (`/users`), Reports/Activity (`/reports/activity`), Audit (`/audit`), Settings (`/settings`), Notifications (`/notifications`).
- **Given** a menu link points to a non-existent route, **then** it must be corrected or removed. The active UI currently contains a stale `/newdocument` link; code also still navigates to stale `/new-documents`. Commented-out links reference `/team`, `/reports/approval-times`, and `/reports/submissions` _(gap)_.
- Unknown routes render the Not Found page (`/not-found`).

---

### EPIC 3 — Document Management

#### US-3.1 — View my documents grouped by status/folder
**As an** authenticated user, **I want to** see my documents organised into folders **so that** I can find published, outbox, archived, and deleted items.

**Acceptance criteria**
- **Given** I open `/documents`, **then** documents load from `api/documents/dashboard?id={status}&userName={userId}` scoped to my user id, presented as tabs.
- Folder/tab definitions (client-side filtering):
  - **Inbox** — documents where I am the `NextActor` and the document is not archived (awaiting my action).
  - **Outbox** — documents I authored (`Author.Id == me`).
  - **Archived** — documents with status Archived that are not my action.
  - **Deleted** — documents with status Deleted.
  - **Draft** — documents with status Draft.
- **Given** I switch tabs, **then** the list reloads for that folder.
- Results are paginated (the client requests a page number and page size; currently page size 100).
- Document status values (`DocStatus` enum): **Published=1**, **Draft=2**, **Outbox=3**, **Deleted=4**, **Archived=5**.

#### US-3.2 — Open a document to view it
**As an** authenticated user, **I want to** open a document **so that** I can read its content and its workflow.

**Acceptance criteria**
- **Given** I select a document, **when** I open `/viewdocument/{DocId}`, **then** the content is streamed from `api/documents/content/{docId}` and rendered as a PDF via PDF.js, alongside its workflow chain (`api/docworkflow/{docId}`) and metadata.
- The viewer supports **next/previous page**, **go-to-page**, and **zoom in/out** (HiDPI-aware rendering).
- Document metadata includes priority, document type, category, status, and next actor.
- Existing signature and comment annotations render at their saved positions (using stored geometry — page, X/Y, width/height, zoom).
- While editing is enabled, annotations can be **dragged, resized, edited (comments), and deleted**. The UI also exposes a document-file attachment control for an existing document.
- **Gap:** the existing-document replacement/update path is incomplete: the signing request carries `ConfirmDocUpdate`, but the editor does not currently set its `documentEdited` flag when a file is attached. Define the intended replacement and versioning behaviour before treating this as a supported workflow.

#### US-3.3 — Upload a document
**As a** document originator, **I want to** upload a document file **so that** I can start a workflow on it.

**Acceptance criteria**
- Document creation is a **3-step wizard** (`/createdocument`, MudStepper): (1) **Document Details**, (2) **Workflow**, (3) **Upload & Sign**. Each step is gated — you cannot advance until the step is valid (details valid, ≥1 approver, ≥1 attached document).
- **Given** I attach a file and complete the wizard, **then** a GUID document reference is generated and the file is uploaded via multipart to `api/upload` (folder `store_{TypeId}`), returning a stored path.
- The full `Document` (author id, next actor = first workflow actor, workflow chain, extra fields, and any signature/comment amendments with their positions) is then POSTed to `api/document`.
- **Maximum upload size is 10 MB** for a document file.
- **Given** the upload fails, **then** I see an error message and the workflow is not started.
- **Given** the upload succeeds, **then** the document is created and the first workflow actor is set as the next actor.

#### US-3.4 — Categorise and prioritise a document
**As a** document originator, **I want to** classify a document by type, category, and priority **so that** it is routed and reported correctly.

**Acceptance criteria**
- Document type, category, and priority values are selectable from server-provided list options (`api/listoptions` / `api/doccategory`).
- **Given** I choose a document category, **then** the applicable category-specific fields and workflow levels are loaded (`api/doccategoryworkflow/category/{docCategory}/levels`).
- Category fields are rendered from the selected category's `ExtraFields`.
- **Gap:** required category fields are not visibly validated by the current details form; the requirement that they must be completed before submit remains an intended behaviour, not a verified implementation.

---

### EPIC 4 — Document Workflow (Routing & Approval)

#### US-4.1 — Define the approval chain for a document
**As a** document originator, **I want to** assign an ordered list of approvers (actors) with levels **so that** the document is routed in sequence.

**Acceptance criteria**
- A workflow is an ordered set of steps, each with an **actor**, a **level**, a **workflow type**, and an **is-final** flag.
- **Given** a document category defines default workflow levels, **then** those levels pre-populate the workflow.
- Approvers are added via a **user typeahead search** (minimum 3 characters, `api/listoptions?type=usersearch`).
- I can **reorder** steps (which renumbers their `Level`) and **delete** steps while building the chain.
- The first actor in the list becomes the document's initial `NextActor`.
- The **last actor is automatically flagged `IsFinal`**; completing the final step closes the workflow.
- Each workflow role/step may be marked **required** and carries a display label _(`WorkflowDto`: `RoleId`, `IsRequired`, `Lable`)_.

#### US-4.2 — Act on a document only when it is my turn
**As an** approver, **I want** edit/sign actions enabled only when I am the current actor **so that** the sequence is enforced.

**Acceptance criteria**
- **Given** I open a document where I am the `NextActor` and it is not archived, **then** editing/signing controls are enabled.
- **Given** I am not the `NextActor`, or the document is Archived, **then** editing/signing is disabled (read-only).
- **Given** I attempt to act when I am not the current actor, **then** the action is blocked with an appropriate message.

#### US-4.3 — Add my signature to a document
**As an** approver, **I want to** place my signature on the document **so that** my approval is recorded visually.

**Acceptance criteria**
- **Given** I am the current actor, **when** I add a signature, **then** a signature attachment is created and positioned on the document.
- The signature image is sourced from my stored user signature; **given** my user details/signature cannot be retrieved, **then** signing is not initialised and I am notified.
- Signature placement supports positioning (drag) on the page.

#### US-4.4 — Add comments/annotations to a document
**As an** approver, **I want to** add comments on the document **so that** I can leave review notes.

**Acceptance criteria**
- **Given** I am the current actor, **when** I add a comment, **then** I am prompted for text and a comment attachment is placed on the document.
- Empty comments are rejected.
- I can edit a comment I have added.
- Attachment types supported on a document are **Signature**, **Comment**, and **Document** content.

#### US-4.5 — Approve and forward to the next actor
**As an** approver, **I want to** approve the document **so that** it advances to the next actor or completes.

**Acceptance criteria**
- **Given** I select "Approve" and I am the current actor, **when** I submit, **then** my annotations are saved via `api/documents/sign/{docId}/{nextActor}` and the document advances to the next actor in the chain.
- **Given** I am the final actor, **when** I approve, **then** there is no next actor and the document reaches its terminal/published state.
- **Given** signing fails, **then** I see an error and the document does not advance.
- **Given** signing succeeds, **then** I see a success confirmation.

#### US-4.6 — Choose an approval outcome (approve / reject / return / review)
**As an** approver, **I want to** record a decision outcome **so that** the document is routed correctly.

**Acceptance criteria**
- The approval action supports the full `ApprovalStatus` set: **Pending (0)**, **Approve (1)**, **Review (2)**, **Return (3)**, **Reject (-2)**.
- **Given** I select **Approve**, **then** the document advances to the next actor (see US-4.5).
- **Given** I select **Reject**, **then** the document does not advance and its status reflects the rejection.
- **Given** I select **Return** or **Review**, **then** the document is routed accordingly _(exact downstream behaviour — e.g. return to previous actor/originator — to be confirmed with product owners)_.
- The chosen status is submitted to `api/documents/sign?Status={approvalStatusId}` together with the forwarded-to actor, document id, and attachments.

#### US-4.7 — See the workflow history/chain of a document
**As an** authenticated user, **I want to** view the workflow steps and who has acted **so that** I understand the document's progress.

**Acceptance criteria**
- The document view lists the ordered workflow actors and levels.
- The current position (next actor) and completed steps are distinguishable.

---

### EPIC 5 — Workflow Templates / Category Configuration

#### US-5.1 — Configure category-based workflow templates
**As an** administrator, **I want to** define workflows per document category **so that** documents of a category follow a standard approval path.

**Acceptance criteria**
- **Given** I open `/workflows`, **then** I select a Document Type → Category (cascading), and the category's approval chain loads from `api/doccategoryworkflow/category/{categoryId}`.
- I can **add** a step (opens a side-dialog to pick a role and set its required flag; posts to `api/doccategoryworkflow`), **reorder** steps up/down (renumbers `Level`), and **delete** steps.
- **When** I save level changes, **then** they are persisted via `PATCH api/doccategoryworkflow/category/{cat}/levels`.
- Each template step carries: role name, role id, level, `IsFinal`, `IsRequired`, `CanUpdate`.
- Category templates seed the ordered workflow when a document of that category is created (US-4.1).

---

### EPIC 6 — User Management

#### US-6.1 — View users in my organisation
**As an** administrator, **I want to** see the list of users **so that** I can manage access.

**Acceptance criteria**
- **Given** I open `/users`, **then** users are loaded (`api/users`) scoped to my organisation and shown in a table.
- I can **quick-filter** the list across name, username, email, and phone.

#### US-6.2 — Add a new user
**As an** administrator, **I want to** create a user **so that** they can access the system.

**Acceptance criteria**
- **Given** I open `/users/add`, **when** I complete the form and submit, **then** the user is created via `api/user`.
- The form is validated with **FluentValidation** (`UserFluentValidator`); all of the following are required:
  - **Name**, **Department**, **Grade**
  - **Username** — minimum 3 characters
  - **Email** — valid email format
  - **Password** — minimum 6 characters
  - **Country code** > 0 and **phone number** > 0 (via the phone control, US-6.3)
  - **Signature** — required, uploaded as base64, **maximum 5 MB**
  - **At least one role** ("Select at least one role")
- Available roles are loaded from `api/listoptions?type=role`.
- The new user is stamped with the current organisation id and `CreatedBy` from the session, and the selected role(s) (each with role id, role name, organisation id).

#### US-6.3 — Capture a valid phone number with country code
**As an** administrator, **I want** a combined country + phone input **so that** phone numbers are captured consistently.

**Acceptance criteria**
- The phone control requires a country to be selected first; the number field is disabled until a country/dial code is chosen.
- The control binds both the country code and the phone number to the user record.

#### US-6.4 — Manage a user's roles
**As an** administrator, **I want to** assign one or more roles to a user **so that** their access matches their responsibilities.

**Acceptance criteria**
- Multiple roles can be selected for a single user.
- Each assigned role records role id, role name, and organisation id.

---

### EPIC 7 — Reporting, Activity & Audit

#### US-7.1 — View document activity report
**As an** administrator, **I want** a document activity report **so that** I can monitor document processing.

**Acceptance criteria**
- A Document Activity page is reachable at `/reports/activity`.
- **Note / gap:** this page appears to be scaffolded; report content, filters (date range, user, status), and export are **not yet implemented** and require specification.

#### US-7.2 — View audit trail
**As an** administrator, **I want** an audit log **so that** I can review who did what and when.

**Acceptance criteria**
- An Audit page is reachable at `/audit`.
- **Note / gap:** appears to be a placeholder; audit entries, filtering, and detail views require specification.

#### US-7.3 — Additional reports _(gap)_
Menu references exist for approval-times (`/reports/approval-times`) and submissions (`/reports/submissions`) but no pages implement them. These are candidate future reports and must either be built or the links removed.

---

### EPIC 8 — Notifications

#### US-8.1 — See my notifications
**As an** authenticated user, **I want** to see notifications (e.g. a document awaiting my action) **so that** I know when to act.

**Acceptance criteria**
- A Notifications page is reachable at `/notifications`.
- **Note / gap:** appears to be a placeholder; notification sourcing, read/unread state, and triggers (e.g. "document forwarded to you") require specification.

---

### EPIC 9 — Settings & Profile

#### US-9.1 — Manage my settings / profile
**As an** authenticated user, **I want** a settings page **so that** I can manage my profile (including my signature).

**Acceptance criteria**
- A Settings page is reachable at `/settings`.
- Because signing depends on a stored user signature, the settings/profile area should allow a user to capture/upload their signature _(inferred requirement; confirm)_.
- **Note / gap:** the settings page appears to be a placeholder; concrete settings require specification.

---

### EPIC 10 — Document Folders

#### US-10.1 — Browse document folders
**As an** authenticated user, **I want to** browse documents by folder **so that** I can organise and locate them.

**Acceptance criteria**
- A Document Folders page is reachable at `/docfolders`.
- **Current state / gap:** this route is a legacy scaffold containing Priority and Document Type selectors plus a no-op Send action; it does **not** browse folders or documents.
- The implemented document-status browsing experience is the tabbed `/documents` page. Confirm whether `/docfolders` should be completed, redirected, or removed.

---

## 4. Domain Glossary

| Term | Meaning |
|---|---|
| **Document** | An uploaded file with metadata (type, category, priority, status, next actor) and an attached workflow. |
| **Workflow** | Ordered list of steps routing a document through approvers. |
| **Workflow step / level** | One position in the chain: actor + level + workflow type + is-final flag. |
| **Actor** | A user assigned to a workflow step; the `NextActor` is whoever must act now. |
| **Attachment / Amendment** | Content placed on a document with geometry (position X/Y, width/height, page, page dimensions, zoom) so it renders in the same spot: `Signature`, `Comment`, or `Document`. |
| **Approval status** | Outcome of an actor's decision (`ApprovalStatus` enum): `Pending=0`, `Approve=1`, `Review=2`, `Return=3`, `Reject=-2`. |
| **Document status (folder)** | `DocStatus` enum: `Published=1`, `Draft=2`, `Outbox=3`, `Deleted=4`, `Archived=5`. |
| **Access level** | Document visibility (`AccessLevel` enum): `Public`, `Private`, `Internal`. |
| **Category / Category field** | Classification of a document; categories can define required extra fields and default workflow levels. |
| **Workflow type** | `WorkFlowType` enum: `Custom` (per-document chain) or `Category` (template-derived). |
| **Role** | Access grouping; baseline `UserRoles`: `User`, `Admin`, `Approver`, `Filler`. Users can hold multiple roles, scoped to an organisation. |
| **Organisation / Department** | Tenant boundary (`OrganisationId`/`EntityId`) for users, roles, documents, and workflow templates; departments subdivide an organisation. |

---

## 5. Non-Functional Requirements

### NFR-1 — Security
- **NFR-1.1** Remove hardcoded default credentials from `Login.razor.cs` before any non-development deployment.
- **NFR-1.2** All API calls must be authenticated with a valid bearer token; unauthorized responses must not expose protected data.
- **NFR-1.3** Complete the refresh-token flow (`GenerateNewCoreToken`) so sessions renew securely instead of failing/expiring silently.
- **NFR-1.4** Sensitive session values (e.g. role name) are encrypted in storage — this practice must be preserved.
- **NFR-1.5** Enforce per-organisation data isolation (multi-tenancy): a user must never see another organisation's users or documents.
- **NFR-1.6** Enforce sequence integrity server-side: only the current `NextActor` may sign/advance a document (client-side gating is not sufficient on its own).
- **NFR-1.7** Add role/route-based authorization: currently any authenticated user can reach any page by URL (no router guards). Sensitive pages (user management, workflow templates, audit) should be gated by role.
- **NFR-1.8** The Google/Microsoft SSO buttons on the login page are **non-functional UI stubs** — either implement the providers or remove the buttons before release.
- **NFR-1.9** Session timeout is currently enforced **client-side only** (2-hour inactivity check); server-side token expiry must be authoritative.

### NFR-2 — Multi-tenancy & State Isolation
- **NFR-2.1** Per-user services (`IHttpService`, `IAuthService`, `IUserSession`, `AppState`, `RequestContext`, `SideDialogService`) MUST remain **scoped** (never singleton) to prevent cross-user state leakage in Blazor Server.

### NFR-3 — Configuration
- **NFR-3.1** The API base URL is currently hardcoded (`https://localhost:7028/`) and MUST be made environment-configurable for deployment.

### NFR-4 — Usability & UI
- **NFR-4.1** Use MudBlazor components consistently for UI.
- **NFR-4.2** Provide clear success/error feedback (snackbars) for every create/upload/sign action.
- **NFR-4.3** Documents render in-browser via PDF.js without requiring download.
- **NFR-4.4** Navigation links must resolve to real routes; remove or implement stale links.
- **NFR-4.5** Dashboard figures, workflow rows, and pending-approval items must be data-backed or explicitly labelled as sample data; production screens must not present placeholder data as live activity.

### NFR-5 — Reliability
- **NFR-5.1** All backend calls must handle failure gracefully (no unhandled exceptions surfaced to the user); failures produce actionable messages.
- **NFR-5.2** Document/user identity should be read from `IUserSession`, not hardcoded per page (current landmine to remediate).

### NFR-6 — Platform / Compatibility
- **NFR-6.1** Target frameworks are fixed: `zxadocsui` and `zxadocsfe` on `net10.0`. Do not change without explicit instruction.
- **NFR-6.2** No automated test suite currently exists; `dotnet build DocsApp.sln` is the primary correctness gate. Adding automated tests is a recommended (out-of-scope) improvement.

---

## 6. Assumptions & Open Questions

1. **Role-based authorization** — Are menu items and actions gated by role, or is the menu the same for everyone? _(Confirm.)_
2. **Rejection behaviour** — On reject, does the document return to the originator, halt, or move to a specific status? _(Confirm downstream flow.)_
3. **Notifications / Audit / Activity reports** — These pages are placeholders. What are the required data sources, filters, and outputs?
4. **Settings/profile** — Confirm whether signature capture, password change, and preferences belong here.
5. **`/docfolders` vs `/documents`** — Clarify the distinction and whether both are needed.
6. **Draft state** — Can originators save a document as Draft before submitting to the workflow?
7. **Refresh token** — Confirm intended session lifetime and renewal UX once the stub is completed.
8. **Backend contract ownership** — Several DTOs/enums come from `pkavule.zxadocslib`; the authoritative contract lives there and should be treated as the source of truth for domain shapes.
9. **Document replacement/versioning** — What should happen when the current actor attaches a replacement document: replace content, create a new version, or prohibit the action after routing begins?
10. **Session persistence choice** — Should the visible Remember switch control persistence, and should the legacy raw-local-storage token path be removed?

---

## 7. Traceability — Requirement → Route/Endpoint

| Requirement | UI route | Key backend endpoint(s) |
|---|---|---|
| EPIC 1 Auth | `/` | `api/user/login` |
| EPIC 2 Dashboard | `/dashboard` | `/statistics/summary`, `/statistics/weekly/summary`, `/statistics/recent-activity` |
| EPIC 3 Documents | `/documents`, `/viewdocument/{DocId?}` | `api/documents`, `api/documents/{docId}`, `api/documents/content/{docId}`, `api/upload` |
| EPIC 4 Workflow / signing | `/createdocument`, `/viewdocument/{DocId?}` | `api/docworkflow/{docId}`, `api/documents/sign/{docId}/{nextActor}` |
| EPIC 5 Category workflows | `/workflows` | `api/doccategoryworkflow`, `api/doccategoryworkflow/category/{docCategory}[/levels]`, `api/doccategory/{value}` |
| EPIC 6 User management | `/users`, `/users/add` | `api/users`, `api/user`, `api/listoptions?type=role` |
| EPIC 7 Reports/Audit | `/reports/activity`, `/audit` | _(TBD — not implemented)_ |
| EPIC 8 Notifications | `/notifications` | _(TBD — not implemented)_ |
| EPIC 9 Settings | `/settings` | _(TBD)_ |
| EPIC 10 Folders | `/docfolders` | _(Legacy scaffold; no folder/document endpoint currently used)_ |

---

_End of document._
