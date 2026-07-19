using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.Components.Custom.Dialogs;

namespace zxadocsui.Components.Pages.Dashboard.Drafts;

// Draft Approvals queue (/drafts/approvals, FR-F9). Lists drafts pending approval; selecting
// one renders its PDF and offers Approve / Reject (reason). Gated on ApproveDraft.
public partial class DraftApprovals
{
    [Inject] private IDraftClientService DraftsApi { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private IDialogService Dialogs { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private readonly List<DraftDto> pending = new();
    private DraftDto? selected;
    private string? previewDataUrl;
    private string? previewError;
    private bool busy;

    protected override async Task OnInitializedAsync()
    {
        if (!await Permissions.Has(Permission.ApproveDraft))
        {
            Snackbar.Add("You don't have permission to approve drafts.", Severity.Warning);
            Nav.NavigateTo("/drafts");
            return;
        }
        await LoadQueue();
    }

    private async Task LoadQueue()
    {
        pending.Clear();
        selected = null; previewDataUrl = null; previewError = null;
        var (ok, page, error) = await DraftsApi.List((int)DraftStatus.PendingApproval, 1, 100);
        if (!ok || page is null) { Snackbar.Add(error ?? "Failed to load the queue.", Severity.Error); return; }
        pending.AddRange(page.Data);
    }

    private async Task Select(DraftDto draft)
    {
        selected = draft;
        previewDataUrl = null; previewError = null;
        var (ok, pdf, error) = await DraftsApi.Preview(draft.Id);
        if (ok && pdf is not null) previewDataUrl = "data:application/pdf;base64," + Convert.ToBase64String(pdf);
        else previewError = error ?? "Preview failed.";
    }

    private async Task Approve()
    {
        if (selected is null) return;
        busy = true;
        try
        {
            var (ok, _, error) = await DraftsApi.Approve(selected.Id);
            if (!ok) { Snackbar.Add(error ?? "Approve failed.", Severity.Error); return; }
            Snackbar.Add("Draft approved.", Severity.Success);
            await LoadQueue();
        }
        finally { busy = false; }
    }

    private async Task Reject()
    {
        if (selected is null) return;
        var reason = await PromptReason();
        if (string.IsNullOrWhiteSpace(reason)) return;

        busy = true;
        try
        {
            var (ok, _, error) = await DraftsApi.Reject(selected.Id, reason!);
            if (!ok) { Snackbar.Add(error ?? "Reject failed.", Severity.Error); return; }
            Snackbar.Add("Draft rejected.", Severity.Success);
            await LoadQueue();
        }
        finally { busy = false; }
    }

    private async Task<string?> PromptReason()
    {
        var parameters = new DialogParameters<TextInputDialog>
        {
            { x => x.Title, "Reject draft" },
            { x => x.Lable, "Reason" },
        };
        var dialog = await Dialogs.ShowAsync<TextInputDialog>("Reject draft", parameters);
        var result = await dialog.Result;
        return result is { Canceled: false, Data: string s } && !string.IsNullOrWhiteSpace(s) ? s : null;
    }
}
