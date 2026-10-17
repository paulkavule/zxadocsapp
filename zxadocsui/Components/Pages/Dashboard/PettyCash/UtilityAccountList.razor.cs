using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.Srevices;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.PettyCash;

/// <summary>Authorised utility accounts (ZD-256). FinanceAdmin registers and edits them; ViewPettyCashFinance reads.</summary>
public partial class UtilityAccountList
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    private List<UtilityAccountDto> accounts = new();
    private bool canEdit;
    private bool loading = true;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        if (!await Permissions.HasAny(Permission.FinanceAdmin, Permission.ViewPettyCashFinance))
        {
            Snackbar.Add("You do not have access to utility accounts.", Severity.Warning);
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
            var (ok, result, error) = await Http.GetAsync<ApiResponse<List<UtilityAccountDto>>>("api/petty-cash/utility-accounts");
            if (!ok || result?.Data is null)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to load utility accounts.", Severity.Error);
                return;
            }
            accounts = result.Data;
        }
        finally
        {
            loading = false;
        }
    }

    private async Task OpenEditor(UtilityAccountDto account)
    {
        var saved = await SideDialog.Show<UtilityAccountEditor, bool?>(
            new Dictionary<string, object> { { nameof(UtilityAccountEditor.Account), account with { } } },
            title: account.Id == 0 ? "New utility account" : $"Edit {account.Provider} {account.AccountNumber}");

        if (saved == true)
        {
            await Load();
            StateHasChanged();
        }
    }
}
