using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.Srevices;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.PettyCash;

/// <summary>Spending categories (ZD-177). FinanceAdmin adds and edits them; ViewPettyCashFinance reads.</summary>
public partial class SpendingCategoryList
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    private List<SpendingCategoryDto> categories = new();
    private List<BillingUnitDto> units = new();
    private bool canEdit;
    private bool loading = true;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        if (!await Permissions.HasAny(Permission.FinanceAdmin, Permission.ViewPettyCashFinance))
        {
            Snackbar.Add("You do not have access to spending categories.", Severity.Warning);
            Nav.NavigateTo("/dashboard");
            return;
        }

        canEdit = await Permissions.Has(Permission.FinanceAdmin);
        await Load();
        StateHasChanged();
    }

    private async Task Load()
    {
        loading = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, result, error) = await Http.GetAsync<ApiResponse<List<SpendingCategoryDto>>>("api/petty-cash/categories");
            if (!ok || result?.Data is null)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to load categories.", Severity.Error);
                return;
            }
            categories = result.Data;

            var (unitsOk, unitList, _) = await Http.GetAsync<ApiResponse<List<BillingUnitDto>>>("api/petty-cash/billing-units");
            units = unitsOk && unitList?.Data is not null ? unitList.Data : new();
        }
        finally
        {
            loading = false;
        }
    }

    private async Task OpenEditor(SpendingCategoryDto category)
    {
        var saved = await SideDialog.Show<SpendingCategoryEditor, bool?>(
            new Dictionary<string, object>
            {
                { nameof(SpendingCategoryEditor.Category), category with { } },
                { nameof(SpendingCategoryEditor.Units), units },
            },
            title: category.Id == 0 ? "New category" : $"Edit {category.Name}");

        if (saved == true)
        {
            await Load();
            StateHasChanged();
        }
    }
}
