using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;

namespace zxadocsui.Components.Pages.Dashboard.Drafts;

// Draft List (/drafts, FR-F6). Server-paginated MudTable of the caller's drafts (server
// scopes to own unless the user has ApproveDraft). Drafts are created from a template, so
// there is no create action here — only a shortcut to the template library.
public partial class DraftList
{
    [Inject] private IDraftClientService DraftsApi { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private MudTable<DraftDto>? table;
    private int statusFilter = -1; // -1 = any

    private async Task<TableData<DraftDto>> LoadData(TableState state, CancellationToken token)
    {
        var (ok, page, error) = await DraftsApi.List(statusFilter, state.Page + 1, state.PageSize);
        if (!ok || page is null)
        {
            Snackbar.Add(error ?? "Failed to load drafts.", Severity.Error);
            return new TableData<DraftDto> { Items = Array.Empty<DraftDto>(), TotalItems = 0 };
        }
        return new TableData<DraftDto> { Items = page.Data, TotalItems = page.TotalCount };
    }

    private async Task OnStatusChanged(int value)
    {
        statusFilter = value;
        if (table is not null) await table.ReloadServerData();
    }

    private void Open(TableRowClickEventArgs<DraftDto> args)
    {
        if (args.Item is not null) Nav.NavigateTo($"/drafts/{args.Item.Id}");
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
