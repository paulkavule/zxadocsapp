using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Drafts;

// Draft Detail (/drafts/{id}, FR-F7). Field values, a live merged HTML preview, and
// status-driven actions: edit/submit (Draft/Rejected), download + Start signing workflow
// (Approved), with the hand-off payload carried to /createdocument via DraftHandoffState.
public partial class DraftDetail
{
    // Same token shape as the backend/editor: {{ key }} = any non-'}' run, trimmed.
    private static readonly Regex TokenPattern = new(@"\{\{\s*([^}]+?)\s*\}\}", RegexOptions.Compiled);

    [Inject] private IDraftClientService DraftsApi { get; set; } = default!;
    [Inject] private ITemplateClientService TemplatesApi { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private DraftHandoffState Handoff { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private DraftDto? draft;
    private string? loadError;
    private bool canManage, canApprove, busy;

    private readonly Dictionary<int, string> fieldLabels = new();
    private TemplateVersionDto? version;          // snapshotted version (field defs)
    private string templateHtml = string.Empty;   // raw template body (with {{key}} tokens)

    private bool rendered;
    private int lastLoadedId = -1;

    // Initial load runs in OnAfterRenderAsync so the token is hydrated before the authed calls;
    // OnParametersSetAsync handles route-param changes (/drafts/5 -> /drafts/8) once ready.
    protected override async Task OnParametersSetAsync()
    {
        if (rendered && Id != lastLoadedId)
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
        lastLoadedId = Id;
        await Session.GetCurrentUser();   // hydrate the auth token before any authed call
        canManage = await Permissions.Has(Permission.CreateDraft);
        canApprove = await Permissions.Has(Permission.ApproveDraft);
        await Load();
    }

    private async Task Load()
    {
        var (ok, data, error) = await DraftsApi.Get(Id);
        if (!ok || data is null) { loadError = error ?? "Draft not found."; draft = null; return; }
        draft = data;

        // Resolve field defs + labels from the snapshotted template version, and load the body
        // for the merged preview.
        var (okV, v, _) = await TemplatesApi.GetVersion(draft.TemplateVersionId);
        version = okV ? v : null;
        fieldLabels.Clear();
        if (version is not null)
        {
            foreach (var f in version.Fields) fieldLabels[f.Id] = f.Label;
            var (okC, html, _) = await TemplatesApi.GetVersionContent(version.Id);
            templateHtml = okC ? html : string.Empty;
        }
    }

    private string FieldLabel(int fieldId) => fieldLabels.TryGetValue(fieldId, out var l) && !string.IsNullOrWhiteSpace(l) ? l : $"Field {fieldId}";

    // Merge the draft's saved values into the template body for an in-browser preview — no
    // LibreOffice/PDF round-trip needed. Unfilled fields are highlighted; unknown tokens kept.
    private string BuildPreview()
    {
        if (version is null || string.IsNullOrEmpty(templateHtml)) return templateHtml;

        var valueByFieldId = (draft?.FieldValues ?? Array.Empty<DraftFieldValueDto>())
            .GroupBy(x => x.TemplateFieldId).ToDictionary(g => g.Key, g => g.Last().Value);
        var byKey = version.Fields
            .Where(f => !string.IsNullOrWhiteSpace(f.Key))
            .GroupBy(f => f.Key.Trim())
            .ToDictionary(g => g.Key, g => g.First());

        return TokenPattern.Replace(templateHtml, m =>
        {
            var key = m.Groups[1].Value.Trim();
            if (!byKey.TryGetValue(key, out var field)) return m.Value; // unknown token — leave visible

            var val = valueByFieldId.GetValueOrDefault(field.Id, string.Empty);
            if (!string.IsNullOrWhiteSpace(val)) return WebUtility.HtmlEncode(val);

            var label = string.IsNullOrWhiteSpace(field.Label) ? key : field.Label;
            return $"<mark style=\"background:#fef08a;color:#713f12;padding:0 3px;border-radius:3px\">{WebUtility.HtmlEncode(label)}</mark>";
        });
    }

    private async Task Submit()
    {
        busy = true;
        try
        {
            var (ok, _, error) = await DraftsApi.Submit(Id);
            if (!ok) { Snackbar.Add(error ?? "Submit failed.", Severity.Error); return; }
            Snackbar.Add("Submitted for approval.", Severity.Success);
            await Load();
        }
        finally { busy = false; }
    }

    private async Task Download()
    {
        busy = true;
        try
        {
            var (ok, pdf, error) = await DraftsApi.Download(Id);
            if (!ok || pdf is null) { Snackbar.Add(error ?? "Download failed.", Severity.Error); return; }
            var name = (draft?.Title ?? "document").Trim() + ".pdf";
            await JS.InvokeVoidAsync("zxFiles.save", name, Convert.ToBase64String(pdf), "application/pdf");
        }
        finally { busy = false; }
    }

    private async Task StartSigning()
    {
        busy = true;
        try
        {
            var (ok, payload, error) = await DraftsApi.GenerateForSigning(Id);
            if (!ok || payload is null) { Snackbar.Add(error ?? "Could not start the signing workflow.", Severity.Error); return; }
            Handoff.Set(payload);
            Nav.NavigateTo("/createdocument");
        }
        finally { busy = false; }
    }

    private static string Label(DraftStatus status) => status switch
    {
        DraftStatus.PendingApproval => "Pending approval",
        DraftStatus.SentToWorkflow => "Sent to workflow",
        _ => status.ToString(),
    };

    private static Color StatusColor(DraftStatus status) => status switch
    {
        DraftStatus.Approved => Color.Success,
        DraftStatus.PendingApproval => Color.Warning,
        DraftStatus.Rejected => Color.Error,
        DraftStatus.SentToWorkflow => Color.Info,
        _ => Color.Default,
    };
}
