using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.Components.Custom.Dialogs;

namespace zxadocsui.Components.Pages.Dashboard.Templates;

// Template Approvals queue (/templates/approvals, FR-F4). Lists templates pending approval;
// selecting one previews its pending version and offers Approve / Reject (reason).
// Gated on ApproveTemplate.
public partial class TemplateApprovals
{
    [Inject] private ITemplateClientService TemplatesApi { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private IDialogService Dialogs { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private readonly List<TemplateDto> pending = new();
    private TemplateDto? selected;
    private TemplateVersionDto? pendingVersion;
    private string previewHtml = string.Empty;
    private bool busy;

    protected override async Task OnInitializedAsync()
    {
        if (!await Permissions.Has(Permission.ApproveTemplate))
        {
            Snackbar.Add("You don't have permission to approve templates.", Severity.Warning);
            Nav.NavigateTo("/templates");
            return;
        }
        await LoadQueue();
    }

    private async Task LoadQueue()
    {
        pending.Clear();
        selected = null; pendingVersion = null; previewHtml = string.Empty;
        var (ok, page, error) = await TemplatesApi.List((int)TemplateStatus.PendingApproval, 0, string.Empty, 1, 100);
        if (!ok || page is null) { Snackbar.Add(error ?? "Failed to load the queue.", Severity.Error); return; }
        pending.AddRange(page.Data);
    }

    private async Task Select(int templateId)
    {
        var (ok, template, error) = await TemplatesApi.Get(templateId);
        if (!ok || template is null) { Snackbar.Add(error ?? "Could not load the template.", Severity.Error); return; }
        selected = template;
        pendingVersion = template.Versions
            .Where(v => v.Status == TemplateStatus.PendingApproval)
            .OrderByDescending(v => v.VersionNo).FirstOrDefault();

        previewHtml = string.Empty;
        if (pendingVersion is not null)
        {
            var (okC, html, _) = await TemplatesApi.GetVersionContent(pendingVersion.Id);
            if (okC) previewHtml = html;
        }
    }

    private async Task Approve()
    {
        if (pendingVersion is null) return;
        busy = true;
        try
        {
            var (ok, _, error) = await TemplatesApi.Approve(pendingVersion.Id);
            if (!ok) { Snackbar.Add(error ?? "Approve failed.", Severity.Error); return; }
            Snackbar.Add("Template approved.", Severity.Success);
            await LoadQueue();
        }
        finally { busy = false; }
    }

    private async Task Reject()
    {
        if (pendingVersion is null) return;
        var reason = await PromptReason();
        if (string.IsNullOrWhiteSpace(reason)) return;

        busy = true;
        try
        {
            var (ok, _, error) = await TemplatesApi.Reject(pendingVersion.Id, reason!);
            if (!ok) { Snackbar.Add(error ?? "Reject failed.", Severity.Error); return; }
            Snackbar.Add("Template rejected.", Severity.Success);
            await LoadQueue();
        }
        finally { busy = false; }
    }

    private async Task<string?> PromptReason()
    {
        var parameters = new DialogParameters<TextInputDialog>
        {
            { x => x.Title, "Reject template" },
            { x => x.Lable, "Reason" },
        };
        var dialog = await Dialogs.ShowAsync<TextInputDialog>("Reject template", parameters);
        var result = await dialog.Result;
        return result is { Canceled: false, Data: string s } && !string.IsNullOrWhiteSpace(s) ? s : null;
    }
}
