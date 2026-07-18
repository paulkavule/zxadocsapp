# ZXADOCS — Legal Templates & Contract Drafting Module
## Front-End Requirements (Blazor Server UI)

**Module:** Legal Document Templates (new nav section in `zxadocsui`).
**Applies to:** `zxadocsui` (Blazor Server, MudBlazor, Tailwind, PDF.js) + shared client services in `zxadocsfe`.
**Companion doc:** `backend-requirements.md` (API contracts).
**Conventions:** MudBlazor components; snackbar feedback on every mutation; all API calls via `IHttpService` (bearer token auto-attached); identity/org read from `IUserSession` (never hardcoded); services stay **scoped**.

---

## 1. Scope

A self-contained "Legal Templates" area where users **upload & approve templates**, **draft legal documents from approved templates**, **approve drafts**, and then **download** or **start a signing workflow**. Starting a signing workflow does **not** introduce new signing UI — it generates a PDF from the draft and routes the user into the **existing Document Workflow module** (`/createdocument`, `requirements.md` EPIC 4), pre-loaded with the generated document, where the user defines the actor chain and signs as today.

---

## 2. Navigation & Routes

Add a "Legal Templates" group to `DashboardLayout` (role-aware; approval/admin items hidden for basic users).

| Route | Page | Purpose |
|---|---|---|
| `/templates` | `TemplateLibrary.razor` | Browse/search approved templates + "My templates" |
| `/templates/new` | `TemplateCreate.razor` | Create & upload a template, define merge fields |
| `/templates/{id}` | `TemplateDetail.razor` | View template, versions, fields, status |
| `/templates/approvals` | `TemplateApprovals.razor` | Approver queue for pending templates |
| `/drafts` | `DraftList.razor` | My/org drafts by status |
| `/drafts/new/{templateId}` | `DraftEditor.razor` | Fill fields → create draft |
| `/drafts/{id}` | `DraftDetail.razor` | View/edit draft, preview, submit, download, start signing workflow |
| `/drafts/approvals` | `DraftApprovals.razor` | Approver queue for pending drafts |

> **No signing UI is added by this module.** Signing reuses the existing `/createdocument` workflow wizard and its actor-chain signing (`requirements.md`, EPIC 4).

---

## 3. Screens + Acceptance Criteria + Test Cases

### FR-F1 — Template Library (`/templates`)
Browse and search approved templates; launch drafting.

**Acceptance criteria**
- Grid/list of templates with name, category, status chip, version, updated date.
- Search box (name/description) + filters (category, status); paginated.
- Basic users see only `Approved` templates (+ their own drafts-in-progress); approvers/admins see all.
- Each approved template has a **"Use / Draft"** action → `/drafts/new/{templateId}`.
- Empty state shown when no templates match.

**Test cases**
- TF1-a: search narrows list; TF1-b: "Use" navigates to draft editor; TF1-c: basic user sees no unapproved templates; TF1-d: pagination works.

### FR-F2 — Create/Upload Template (`/templates/new`)
Upload a file and define merge fields.

**Acceptance criteria**
- Form: name (required), category (required, from `api/template-categories`), description; file upload (PDF/DOCX, ≤10 MB) with client-side type/size validation and progress.
- **Merge-field builder:** add rows (key, label, type, required, options for dropdown, order); key must be unique and slug-formatted; reorder/remove rows.
- Save creates the template (`Draft`) and uploads version #1; success snackbar → navigate to `/templates/{id}`.
- Invalid file type/size blocked before upload with an inline error.
- "Submit for approval" available after save.

**Test cases**
- TF2-a: upload 12 MB → blocked; TF2-b: duplicate field key → inline error; TF2-c: save then submit → status becomes `PendingApproval`; TF2-d: upload progress renders.

### FR-F3 — Template Detail & Versions (`/templates/{id}`)
Inspect a template and its lifecycle.

**Acceptance criteria**
- Shows metadata, current status chip, field list, and **version history** (version no, status, approver, date).
- Owner/admin can edit (creates a new version), submit, or archive; actions gated by status and role.
- In-browser **PDF preview** of the current version via PDF.js.
- "Use / Draft" shown only when status = `Approved`.

**Test cases**
- TF3-a: editing approved template creates new `Draft` version while current stays approved; TF3-b: non-owner sees no edit/submit actions; TF3-c: preview renders.

### FR-F4 — Template Approvals (`/templates/approvals`)
Approver queue.

**Acceptance criteria**
- Lists `PendingApproval` templates for the org; open → preview + Approve / Reject.
- Reject requires a reason (dialog); Approve confirms then updates status with snackbar.
- Item leaves the queue after a decision; queue empty-state shown.
- Page hidden from users without the approver role.

**Test cases**
- TF4-a: reject without reason → blocked; TF4-b: approve → item removed, template becomes usable; TF4-c: basic user cannot reach page (redirect/hidden).

### FR-F5 — Draft Editor (`/drafts/new/{templateId}`)
Fill merge fields to create a legal document.

**Acceptance criteria**
- Renders a dynamic form generated from the template's fields (correct input per `TemplateFieldType`: text, multiline, number, currency, date, boolean, dropdown, party).
- Required-field and type validation before submit; per-field error messages.
- **Live preview** (or on-demand preview) of the merged document via `api/drafts/{id}/preview` in the PDF.js viewer.
- Save creates/updates the draft (`Draft`); "Submit for approval" transitions it.
- Only reachable for `Approved` templates; otherwise redirect with message.

**Test cases**
- TF5-a: submit with empty required field → blocked with error; TF5-b: date field rejects invalid date; TF5-c: dropdown limits to defined options; TF5-d: preview reflects entered values.

### FR-F6 — Draft List (`/drafts`)
Manage drafts by status.

**Acceptance criteria**
- Tabs/filters by `DraftStatus` (Draft, PendingApproval, Approved, Rejected, SentToWorkflow); org/user scoped.
- Row actions contextual to status: edit (Draft), view, download (Approved/SentToWorkflow), **Start signing workflow** (Approved).
- Quick search across title/template/status.

**Test cases**
- TF6-a: only `Approved`/`SentToWorkflow` rows expose Download; TF6-b: only `Approved` rows expose Start-signing-workflow; TF6-c: status filter works.

### FR-F7 — Draft Detail (`/drafts/{id}`)
Central hub for one draft.

**Acceptance criteria**
- Shows title, source template/version, status, field values, and PDF preview.
- Actions by status: edit & submit (Draft); download (Approved/SentToWorkflow); **Start signing workflow** (Approved).
- **Download** streams the final PDF (`api/drafts/{id}/download`); disabled unless status allows, with tooltip explaining why.
- All actions give success/error snackbars.

**Test cases**
- TF7-a: Download hidden/disabled for `Draft`; TF7-b: Approved draft downloads a merged PDF; TF7-c: `SentToWorkflow` draft shows a link/reference to the created workflow document.

### FR-F8 — Start signing workflow (hand-off to Document Workflow module)
Generate a PDF from the approved draft and route the user into the existing workflow wizard, pre-loaded and ready to sign.

**Acceptance criteria**
- Available only when draft `Status = Approved`.
- **When** the user clicks **"Start signing workflow"**, **then** the UI calls `POST api/drafts/{id}/generate-for-signing`, shows a busy indicator, and receives `{ documentReference, filePath, fileName, title, typeId?, categoryId? }`.
- **On success**, the user is **automatically routed to the Document Workflow wizard** (`/createdocument`) with the generated document **pre-loaded** — the file is already stored server-side, so the wizard skips/pre-fills the upload step and lands the user ready to **define the workflow** (actor chain, levels) and sign per `requirements.md` EPIC 4.
- The user proceeds to define approvers and sign entirely within the existing module; **no signing UI is duplicated here**.
- **On error**, an error snackbar is shown and the user stays on the draft (draft remains `Approved`).
- After a successful hand-off the draft reflects `SentToWorkflow` and links to the created workflow document.

**Test cases**
- TF8-a: button hidden/disabled unless `Approved`; TF8-b: click → generate call made, busy state shown; TF8-c: on success → navigated to `/createdocument` with the document pre-loaded (no manual re-upload); TF8-d: workflow wizard opens on the workflow-definition step; TF8-e: generate failure → error snackbar, stays on draft.

### FR-F9 — Draft Approvals (`/drafts/approvals`)
Approver queue for drafts (mirrors FR-F4).

**Acceptance criteria**
- Lists `PendingApproval` drafts; preview + Approve / Reject (reason required on reject).
- Approved drafts become downloadable / signing-ready; queue updates live; role-gated.

**Test cases**
- TF9-a: approve → draft `Approved`; TF9-b: reject with reason → draft `Rejected`, creator notified; TF9-c: page hidden for non-approvers.

---

## 4. Cross-Cutting UI Requirements

- **NFR-F1 Role-aware nav:** template/draft approval and category management links visible only to Approver/Admin roles (adds proper route guarding — see main `requirements.md` NFR-1.7).
- **NFR-F2 State from session:** user id, org id, roles read from `IUserSession`; no hardcoded identity.
- **NFR-F3 Feedback:** every create/upload/submit/approve/sign action shows a snackbar; destructive actions (reject, cancel, archive) use a confirm dialog.
- **NFR-F4 Validation:** client-side validation mirrors backend rules (file type/size, required fields, field types, emails) using MudForm/FluentValidation; server remains source of truth.
- **NFR-F5 Viewer reuse:** reuse the existing PDF.js viewer for template/draft previews. Signature capture and annotation are used only after hand-off, inside the existing Document Workflow module (not re-implemented here).
- **NFR-F6 Loading/empty/error states:** every list and viewer has explicit loading skeletons, empty states, and error fallbacks; the hand-off action shows a busy indicator until routing completes.
- **NFR-F7 Responsive:** all pages usable on tablet.
- **NFR-F8 Consistency:** follow existing MudBlazor + Tailwind styling and the brand green (`#1f7b4d`); preserve existing naming conventions.

---

## 5. Client Service / DTO Additions (`zxadocsfe`)

- `ITemplateService` — CRUD/list/submit/approve/reject/archive templates; upload version; manage fields & categories.
- `IDraftService` — create/update/list drafts, preview, submit/approve/reject, download, and **`GenerateForSigning(draftId)`** (returns the doc reference/path payload for hand-off).
- **No signing service is added** — the hand-off routes into the existing Document Workflow wizard, which uses the existing workflow/signing services (`api/document`, `api/docworkflow`, `api/documents/sign`).
- DTOs mirroring §2 of `backend-requirements.md`: `TemplateDto`, `TemplateVersionDto`, `TemplateFieldDto`, `DraftDto`, `DraftFieldValueDto`, plus the enums. Prefer sharing domain types from `pkavule.zxadocslib` where they already exist.

---

_End of front-end requirements._
