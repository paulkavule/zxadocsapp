using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.Srevices;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.PettyCash;

/// <summary>
/// Wallet balances (ZD-273). FinanceAdmin can suspend or reactivate a wallet and set its low-balance
/// threshold (ZD-312), and assign users to it (ZD-234); ViewPettyCashFinance reads.
/// </summary>
public partial class WalletList
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    private List<WalletDto> wallets = new();
    private List<WalletAssignmentDto> assignments = new();
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

            var (assignedOk, assigned, _) = await Http.GetAsync<ApiResponse<List<WalletAssignmentDto>>>("api/petty-cash/wallet-assignments");
            assignments = assignedOk && assigned?.Data is not null ? assigned.Data : new();
        }
        finally
        {
            loading = false;
        }
    }

    private async Task EditThreshold(WalletDto wallet)
    {
        var saved = await SideDialog.Show<WalletThresholdEditor, bool?>(
            new Dictionary<string, object> { { nameof(WalletThresholdEditor.Wallet), wallet } },
            title: $"Threshold for {wallet.BillingUnitCode}");

        if (saved == true)
        {
            await Load();
            StateHasChanged();
        }
    }

    private async Task Assign(WalletDto wallet)
    {
        var saved = await SideDialog.Show<WalletAssignEditor, bool?>(
            new Dictionary<string, object> { { nameof(WalletAssignEditor.Wallet), wallet } },
            title: $"Assign a user to {wallet.BillingUnitCode}");

        if (saved == true)
        {
            await Load();
            StateHasChanged();
        }
    }

    private async Task Unassign(WalletAssignmentDto assignment)
    {
        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (ok, _, error) = await Http.ExecuteRequestAsync<ApiResponse<int>>(
            HttpVerb.Delete, $"api/petty-cash/wallet-assignments/{assignment.UserId}");

        Snackbar.Add(ok ? $"{assignment.UserName} unassigned." : ErrorMessage.Extract(error) ?? "Failed to unassign.",
            ok ? Severity.Success : Severity.Error);
        await Load();
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
