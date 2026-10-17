using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.Srevices;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.PettyCash;

/// <summary>Billing units (ZD-254). FinanceAdmin adds and edits them; ViewPettyCashFinance reads.</summary>
public partial class BillingUnitList
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    private List<BillingUnitDto> units = new();
    private bool canEdit;
    private bool loading = true;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        if (!await Permissions.HasAny(Permission.FinanceAdmin, Permission.ViewPettyCashFinance))
        {
            Snackbar.Add("You do not have access to billing units.", Severity.Warning);
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
            var (ok, result, error) = await Http.GetAsync<ApiResponse<List<BillingUnitDto>>>("api/petty-cash/billing-units");
            if (!ok || result?.Data is null)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to load billing units.", Severity.Error);
                return;
            }
            units = result.Data;
        }
        finally
        {
            loading = false;
        }
    }

    private async Task OpenEditor(BillingUnitDto unit)
    {
        var saved = await SideDialog.Show<BillingUnitEditor, bool?>(
            new Dictionary<string, object> { { nameof(BillingUnitEditor.Unit), unit with { } } },
            title: unit.Id == 0 ? "New billing unit" : $"Edit {unit.Code}");

        if (saved == true)
        {
            await Load();
            StateHasChanged();
        }
    }

    private static string Money(decimal? amount) => amount is null ? "policy" : amount.Value.ToString("N0");
}
