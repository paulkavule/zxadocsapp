using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.Components.Pages.Dashboard.Templates;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Drafts;

// Draft Editor (/drafts/new/{templateId}, /drafts/{id}/edit — FR-F5). Left column: a form
// generated from the template version's fields (one input per TemplateFieldType). Right
// column: a LIVE preview of the template body with the current values merged in, updated on
// every edit. Saves the draft (create or update) and submits for approval.
public partial class DraftEditor
{
    // Same token shape the backend HtmlTokenMerger uses: {{ key }} where the key is any run of
    // non-'}' characters (trimmed) — so multi-word keys like {{contract date}} merge too.
    private static readonly Regex TokenPattern = new(@"\{\{\s*([^}]+?)\s*\}\}", RegexOptions.Compiled);

    [Inject] private ITemplateClientService TemplatesApi { get; set; } = default!;
    [Inject] private IDraftClientService DraftsApi { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private TemplateVersionDto? version;   // the approved version being filled
    private int draftId;                   // 0 until saved/loaded
    private string title = string.Empty;
    private string? loadError;
    private bool busy;

    private readonly Dictionary<int, string> values = new();
    private string templateHtml = string.Empty;   // raw template body (with {{key}} tokens)

    private bool rendered;
    private int lastId = -1, lastTemplateId = -1;

    // The initial load runs in OnAfterRenderAsync: it is the first point JS interop is available,
    // so the auth token can be hydrated from ProtectedLocalStorage before any authed API call
    // (loading here in OnParametersSetAsync would fire tokenless on a cold reload/deep-link -> 401).
    // OnParametersSetAsync still handles route changes (new template / editing a different draft)
    // once the session is ready.
    protected override async Task OnParametersSetAsync()
    {
        if (rendered && (Id != lastId || TemplateId != lastTemplateId))
            await LoadAll();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        rendered = true;
        await LoadAll();
        StateHasChanged();
    }

    private async Task LoadAll()
    {
        lastId = Id;
        lastTemplateId = TemplateId;
        await Session.GetCurrentUser();   // hydrate the auth token before any authed call
        if (Id > 0) await LoadExisting();
        else await LoadForTemplate();
    }

    private async Task LoadForTemplate()
    {
        var (ok, template, error) = await TemplatesApi.Get(TemplateId);
        if (!ok || template is null) { loadError = error ?? "Template not found."; return; }
        if (template.Status != TemplateStatus.Approved || template.CurrentVersion is null)
        {
            Snackbar.Add("Drafts can only be started from an approved template.", Severity.Warning);
            Nav.NavigateTo($"/templates/{TemplateId}");
            return;
        }
        version = template.CurrentVersion;
        await LoadTemplateBody(version.Id);
    }

    private async Task LoadExisting()
    {
        var (ok, draft, error) = await DraftsApi.Get(Id);
        if (!ok || draft is null) { loadError = error ?? "Draft not found."; return; }
        draftId = draft.Id;
        title = draft.Title;
        foreach (var v in draft.FieldValues) values[v.TemplateFieldId] = v.Value;

        // The draft snapshots a template version id; fetch that version for its field defs.
        var (okV, v2, verr) = await TemplatesApi.GetVersion(draft.TemplateVersionId);
        if (!okV || v2 is null) { loadError = verr ?? "Template version not found."; return; }
        version = v2;
        await LoadTemplateBody(version.Id);
    }

    private async Task LoadTemplateBody(int versionId)
    {
        var (ok, html, _) = await TemplatesApi.GetVersionContent(versionId);
        // Stored image URLs are canonical and unsigned; a preview iframe cannot send a bearer
        // token, so they must be exchanged for signed ones or every image renders broken.
        templateHtml = ok ? await TemplateHtml.WithDisplayableImagesAsync(html, TemplatesApi) : string.Empty;
    }

    // ---- value helpers ----
    private string Get(int fieldId) => values.TryGetValue(fieldId, out var v) ? v : string.Empty;
    private void Set(int fieldId, string value) => values[fieldId] = value ?? string.Empty;

    private DateTime? GetDate(int fieldId) =>
        DateTime.TryParse(Get(fieldId), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    private void SetDate(int fieldId, DateTime? d) => values[fieldId] = d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    private bool GetBool(int fieldId) => Get(fieldId).Equals("true", StringComparison.OrdinalIgnoreCase);
    private void SetBool(int fieldId, bool b) => values[fieldId] = b ? "true" : "false";

    // ---- actions ----
    private async Task<bool> SaveDraft()
    {
        if (string.IsNullOrWhiteSpace(title)) { Snackbar.Add("A title is required.", Severity.Warning); return false; }
        if (version is null) return false;

        var inputs = values.Select(kv => new DraftFieldValueInput { TemplateFieldId = kv.Key, Value = kv.Value }).ToList();
        busy = true;
        try
        {
            if (draftId == 0)
            {
                var (ok, draft, error) = await DraftsApi.Create(new CreateDraftRequest
                {
                    TemplateVersionId = version.Id,
                    Title = title.Trim(),
                    FieldValues = inputs,
                });
                if (!ok || draft is null) { Snackbar.Add(error ?? "Could not save the draft.", Severity.Error); return false; }
                draftId = draft.Id;
            }
            else
            {
                var (ok, _, error) = await DraftsApi.Update(draftId, new UpdateDraftRequest { Title = title.Trim(), FieldValues = inputs });
                if (!ok) { Snackbar.Add(error ?? "Could not update the draft.", Severity.Error); return false; }
            }
            Snackbar.Add("Draft saved.", Severity.Success);
            return true;
        }
        finally { busy = false; }
    }

    // Build the live preview: the template body with each {{key}} replaced by its current value
    // (HTML-encoded, mirroring the backend merge). Fields not yet filled are shown as a highlighted
    // placeholder so the user can see what's outstanding; unknown tokens are left intact. This is a
    // rendering aid only — it never mutates the saved draft or the final rendered PDF.
    private string BuildPreview()
    {
        if (version is null || string.IsNullOrEmpty(templateHtml)) return templateHtml;

        var byKey = version.Fields
            .Where(f => !string.IsNullOrWhiteSpace(f.Key))
            .GroupBy(f => f.Key.Trim())
            .ToDictionary(g => g.Key, g => g.First());

        return TokenPattern.Replace(templateHtml, m =>
        {
            var key = m.Groups[1].Value.Trim();
            if (!byKey.TryGetValue(key, out var field)) return m.Value; // unknown token — leave visible

            var value = Get(field.Id);
            if (!string.IsNullOrWhiteSpace(value)) return WebUtility.HtmlEncode(value);

            var label = string.IsNullOrWhiteSpace(field.Label) ? key : field.Label;
            return $"<mark style=\"background:#fef08a;color:#713f12;padding:0 3px;border-radius:3px\">{WebUtility.HtmlEncode(label)}</mark>";
        });
    }

    private async Task SubmitDraft()
    {
        if (draftId == 0 && !await SaveDraft()) return;
        var (ok, _, error) = await DraftsApi.Submit(draftId);
        if (!ok) { Snackbar.Add(error ?? "Submit failed.", Severity.Error); return; }
        Snackbar.Add("Submitted for approval.", Severity.Success);
        Nav.NavigateTo($"/drafts/{draftId}");
    }
}
