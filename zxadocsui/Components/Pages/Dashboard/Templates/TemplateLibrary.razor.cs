using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Templates;

// Template Library (/templates, FR-F1). Server-paginated MudTable with search + status +
// category filters. Server enforces visibility (Approved + own for non-approvers); the UI
// only gates the "New template" action on CreateTemplate.
public partial class TemplateLibrary
{
    [Inject] private ITemplateClientService TemplatesApi { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private MudTable<TemplateDto>? table;
    private string search = string.Empty;
    private int statusFilter = -1;   // -1 = any
    private int categoryFilter = 0;  // 0 = any
    private bool canCreate;
    private List<TemplateCategoryDto> categories = new();

    // Permission check + authed loads run in OnAfterRenderAsync so the token is hydrated first;
    // in OnInitializedAsync the tokenless call would 401 and hide the "New template" button.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        await Session.GetCurrentUser();   // hydrate the auth token before any authed call
        canCreate = await Permissions.Has(Permission.CreateTemplate);
        var (ok, cats, _) = await TemplatesApi.GetCategories();
        if (ok) categories = cats.ToList();
        StateHasChanged();
    }

    private async Task<TableData<TemplateDto>> LoadData(TableState state, CancellationToken token)
    {
        // MudTable pages are 0-based; the API is 1-based.
        var (ok, page, error) = await TemplatesApi.List(statusFilter, categoryFilter, search, state.Page + 1, state.PageSize);
        if (!ok || page is null)
        {
            Snackbar.Add(error ?? "Failed to load templates.", Severity.Error);
            return new TableData<TemplateDto> { Items = Array.Empty<TemplateDto>(), TotalItems = 0 };
        }
        return new TableData<TemplateDto> { Items = page.Data, TotalItems = page.TotalCount };
    }

    private Task Reload() => table?.ReloadServerData() ?? Task.CompletedTask;

    private async Task OnSearchChanged(string value) { search = value; await Reload(); }
    private async Task OnStatusChanged(int value) { statusFilter = value; await Reload(); }
    private async Task OnCategoryChanged(int value) { categoryFilter = value; await Reload(); }

    private void Open(TableRowClickEventArgs<TemplateDto> args)
    {
        if (args.Item is not null) Nav.NavigateTo($"/templates/{args.Item.Id}");
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
