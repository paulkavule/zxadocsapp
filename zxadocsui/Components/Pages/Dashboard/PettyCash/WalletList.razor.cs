using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.PettyCash;

/// <summary>Wallet balances (ZD-273). FinanceAdmin can suspend or reactivate a wallet; ViewPettyCashFinance reads.</summary>
public partial class WalletList
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private List<WalletDto> wallets = new();
    private bool canEdit;
    private bool loading = true;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        if (!await Permissions.HasAny(Permission.FinanceAdmin, Permission.ViewPettyCashFinance))
        {
            Snackbar.Add("You do not have access to wallets.", Severity.Warning);
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
            var (ok, result, error) = await Http.GetAsync<ApiResponse<List<WalletDto>>>("api/petty-cash/wallets");
            if (!ok || result?.Data is null)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to load wallets.", Severity.Error);
                return;
            }
            wallets = result.Data;
        }
        finally
        {
            loading = false;
        }
    }

    private async Task SetStatus(WalletDto wallet, bool isActive)
    {
        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (ok, _, error) = await Http.ExecuteRequestAsync<ApiResponse<WalletDto>>(
            HttpVerb.Patch, $"api/petty-cash/wallets/{wallet.Id}/status", new { IsActive = isActive });

        Snackbar.Add(ok ? (isActive ? "Wallet reactivated." : "Wallet suspended.") : ErrorMessage.Extract(error) ?? "Failed to change the wallet.",
            ok ? Severity.Success : Severity.Error);
        await Load();
    }
}
