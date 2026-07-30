using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.Components.Custom;
using zxadocsui.Components.Custom.Dialogs;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Templates;

// Template Detail (/templates/{id}, FR-F3). Shows metadata, merge fields, version history
// and a preview; supports editing (new version via the rich text editor) and the approval
// state machine (submit/approve/reject/archive), all gated by permission + status.
public partial class TemplateDetail
{
    [Inject] private ITemplateClientService TemplatesApi { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private IDialogService Dialogs { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private TemplateDto? template;
    private string? loadError;
    private bool canManage, canApprove, canDraft, busy, editing;

    private RichTextEditor? editorRef;
    private string editHtml = string.Empty;
    private string? editDelta;
    private string previewHtml = string.Empty;
    private string? previewError;

    // The version the fields/preview reflect: the current (approved) one, else the latest.
    private TemplateVersionDto? ActiveVersion =>
        template?.CurrentVersion ?? template?.Versions.OrderByDescending(v => v.VersionNo).FirstOrDefault();
    private IReadOnlyList<TemplateFieldDto> Fields => ActiveVersion?.Fields ?? Array.Empty<TemplateFieldDto>();
    private TemplateVersionDto? draftVersion =>
        template?.Versions.Where(v => v.Status == TemplateStatus.Draft).OrderByDescending(v => v.VersionNo).FirstOrDefault();
    private TemplateVersionDto? pendingVersion =>
        template?.Versions.Where(v => v.Status == TemplateStatus.PendingApproval).OrderByDescending(v => v.VersionNo).FirstOrDefault();

    private bool rendered;
    private int lastLoadedId = -1;

    // The initial load runs in OnAfterRenderAsync: that's the first point JS interop is available,
    // so the auth token can be hydrated from ProtectedLocalStorage before any authed API call
    // (loading here in OnParametersSetAsync would fire tokenless on a cold reload/deep-link -> 401).
    // OnParametersSetAsync still handles route-param changes (/templates/5 -> /templates/8) once
    // the session is ready.
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
        canManage = await Permissions.Has(Permission.CreateTemplate);
        canApprove = await Permissions.Has(Permission.ApproveTemplate);
        canDraft = await Permissions.Has(Permission.CreateDraft);
        await Load();
    }

    private async Task Load()
    {
        var (ok, data, error) = await TemplatesApi.Get(Id);
        if (!ok || data is null) { loadError = error ?? "Template not found."; template = null; return; }
        template = data;
        await LoadPreview();
    }

    private async Task LoadPreview()
    {
        previewHtml = string.Empty; previewError = null;
        var version = ActiveVersion;
        if (version is null) { previewError = "No version yet."; return; }

        var (ok, html, error) = await TemplatesApi.GetVersionContent(version.Id);
        // Stored image URLs are host-relative; inside the iframe they would resolve against
        // this app rather than the API, so sign them for display.
        if (ok) previewHtml = await TemplateHtml.WithDisplayableImagesAsync(html, TemplatesApi);
        else previewError = error ?? "Preview unavailable.";
    }

    private async Task StartEdit()
    {
        var version = ActiveVersion;
        editHtml = string.Empty;
        editDelta = null;
        if (version is not null)
        {
            var (ok, html, _) = await TemplatesApi.GetVersionContent(version.Id);
            if (ok)
            {
                // The Delta restores the document exactly, including tables; the body HTML is the
                // fallback for versions saved before it was stored.
                editDelta = DocumentHtml.ExtractDelta(html);
                editHtml = DocumentHtml.ExtractBody(html);
            }
        }
        editing = true;
    }

    private async Task SaveVersion()
    {
        if (editorRef is null) return;
        var body = await editorRef.GetHtmlAsync();
        var delta = await editorRef.GetDeltaAsync();
        if (TemplateHtml.IsBodyEmpty(body)) { Snackbar.Add("The contract body is empty.", Severity.Warning); return; }

        busy = true;
        try
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(DocumentHtml.Wrap(template!.Name, body, delta));
            using var ms = new MemoryStream(bytes);
            var (ok, version, error) = await TemplatesApi.UploadVersion(template.Id, ms, "template.html", new Progress<double>());
            if (!ok || version is null) { Snackbar.Add(error ?? "Could not save the version.", Severity.Error); return; }

            // Carry the existing merge fields onto the new version.
            if (Fields.Count > 0)
                await TemplatesApi.SetFields(version.Id, new SetFieldsRequest { Fields = Fields.ToList() });

            Snackbar.Add("New version saved.", Severity.Success);
            editing = false;
            await Load();
        }
        finally { busy = false; }
    }

    private Task Submit() => Act(async () => { var r = await TemplatesApi.Submit(draftVersion!.Id); return (r.ok, r.error); }, "Submitted for approval.");
    private Task Approve() => Act(async () => { var r = await TemplatesApi.Approve(pendingVersion!.Id); return (r.ok, r.error); }, "Approved.");
    private Task Archive() => Act(async () => { var r = await TemplatesApi.Archive(template!.Id); return (r.ok, r.error); }, "Archived.");

    private async Task Reject()
    {
        var reason = await PromptReason("Reject version");
        if (string.IsNullOrWhiteSpace(reason)) return;
        await Act(async () => { var r = await TemplatesApi.Reject(pendingVersion!.Id, reason!); return (r.ok, r.error); }, "Rejected.");
    }

    private async Task<string?> PromptReason(string title)
    {
        var parameters = new DialogParameters<TextInputDialog>
        {
            { x => x.Title, title },
            { x => x.Lable, "Reason" },
        };
        var dialog = await Dialogs.ShowAsync<TextInputDialog>(title, parameters);
        var result = await dialog.Result;
        return result is { Canceled: false, Data: string s } && !string.IsNullOrWhiteSpace(s) ? s : null;
    }

    private async Task Act(Func<Task<(bool ok, string? error)>> action, string success)
    {
        busy = true;
        try
        {
            var (ok, error) = await action();
            if (!ok) { Snackbar.Add(error ?? "Action failed.", Severity.Error); return; }
            Snackbar.Add(success, Severity.Success);
            await Load();
        }
        finally { busy = false; }
    }

    private static Color StatusColor(TemplateStatus status) => status switch
    {
        TemplateStatus.Approved => Color.Success,
        TemplateStatus.PendingApproval => Color.Warning,
        TemplateStatus.Rejected => Color.Error,
        TemplateStatus.Archived => Color.Dark,
        _ => Color.Default,
    };
}
