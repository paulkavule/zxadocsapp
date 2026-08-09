using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard;

// Document activity report (/reports/activity). Server-paginated MudTable over
// GET /api/document-activity, filterable by document id and actor. The organisation is
// taken from the caller's token server-side, so nothing scopes it from here.
public partial class DocumentActivity
{
    [Inject] private IActivityClientService ActivityApi { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private MudTable<DocumentActivityDto>? table;
    private string documentIdFilter = string.Empty;
    private string actorFilter = string.Empty;

    private bool HasFilters =>
        !string.IsNullOrWhiteSpace(documentIdFilter) || !string.IsNullOrWhiteSpace(actorFilter);

    // Hydrate the token before the table's first server call; without it the load 401s.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        await Session.GetCurrentUser();
        await Reload();
    }

    private async Task<TableData<DocumentActivityDto>> LoadData(TableState state, CancellationToken token)
    {
        // A part-typed or non-numeric document id filters nothing rather than erroring.
        _ = int.TryParse(documentIdFilter, out var documentId);

        // MudTable pages are 0-based; the API is 1-based.
        var (ok, page, error) = await ActivityApi.ListDocumentActivity(
            documentId, actorFilter, state.Page + 1, state.PageSize);

        if (!ok || page is null)
        {
            Snackbar.Add(error ?? "Failed to load document activity.", Severity.Error);
            return new TableData<DocumentActivityDto> { Items = Array.Empty<DocumentActivityDto>(), TotalItems = 0 };
        }

        return new TableData<DocumentActivityDto> { Items = page.Data, TotalItems = page.TotalCount };
    }

    private Task Reload() => table?.ReloadServerData() ?? Task.CompletedTask;

    private async Task OnDocumentIdChanged(string value) { documentIdFilter = value; await Reload(); }
    private async Task OnActorChanged(string value) { actorFilter = value; await Reload(); }

    private async Task ClearFilters()
    {
        documentIdFilter = string.Empty;
        actorFilter = string.Empty;
        await Reload();
    }

    // Action is free text: "Created" plus the ApprovalStatus names DocumentService records.
    private static Color ActionColor(string action) => action switch
    {
        "Approve" => Color.Success,
        "Reject" => Color.Error,
        "Pending" => Color.Warning,
        "Review" => Color.Info,
        "Return" => Color.Warning,
        _ => Color.Default,
    };
}
