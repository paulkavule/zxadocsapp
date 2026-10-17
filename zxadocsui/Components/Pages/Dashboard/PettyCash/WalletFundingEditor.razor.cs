using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocsui.Srevices;

namespace zxadocsui.Components.Pages.Dashboard.PettyCash;

/// <summary>Initiates a wallet funding (ZD-291) in the side panel; closes with true when it saved.</summary>
public partial class WalletFundingEditor
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    private WalletFundingDto funding = new();
    private List<WalletDto> wallets = new();
    private DateTime? date = DateTime.Today;
    private bool busy;

    protected override async Task OnInitializedAsync()
    {
        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (ok, result, _) = await Http.GetAsync<ApiResponse<List<WalletDto>>>("api/petty-cash/wallets");
        wallets = ok && result?.Data is not null ? result.Data.Where(w => w.IsActive).ToList() : new();
    }

    private async Task Save()
    {
        busy = true;
        try
        {
            funding.FundingDate = DateOnly.FromDateTime(date!.Value);
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, _, error) = await Http.ExecuteRequestAsync<ApiResponse<WalletFundingDto>>(HttpVerb.Post, "api/petty-cash/fundings", funding);

            if (!ok)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to submit the funding.", Severity.Error);
                return;
            }

            Snackbar.Add("Funding submitted for approval.", Severity.Success);
            SideDialog.Close(true);
        }
        finally
        {
            busy = false;
        }
    }
}
