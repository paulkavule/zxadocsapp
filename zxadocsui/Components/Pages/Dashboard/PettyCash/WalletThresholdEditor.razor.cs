using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocsui.Srevices;

namespace zxadocsui.Components.Pages.Dashboard.PettyCash;

/// <summary>Sets a wallet's low-balance threshold (ZD-312) in the side panel; closes with true when it saved.</summary>
public partial class WalletThresholdEditor
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    [Parameter] public WalletDto Wallet { get; set; } = new();

    private decimal? threshold;
    private bool busy;

    protected override void OnInitialized() => threshold = Wallet.LowBalanceThreshold;

    private async Task Save()
    {
        busy = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, _, error) = await Http.ExecuteRequestAsync<ApiResponse<WalletDto>>(
                HttpVerb.Put, $"api/petty-cash/wallets/{Wallet.Id}/threshold", new { LowBalanceThreshold = threshold });

            if (!ok)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to save the threshold.", Severity.Error);
                return;
            }

            Snackbar.Add("Threshold saved.", Severity.Success);
            SideDialog.Close(true);
        }
        finally
        {
            busy = false;
        }
    }
}
