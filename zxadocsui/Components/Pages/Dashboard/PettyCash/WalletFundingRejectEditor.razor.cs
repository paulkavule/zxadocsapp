using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocsui.Srevices;

namespace zxadocsui.Components.Pages.Dashboard.PettyCash;

/// <summary>Rejects a pending funding with a reason (ZD-291); closes with true when it saved.</summary>
public partial class WalletFundingRejectEditor
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    [Parameter] public WalletFundingDto Funding { get; set; } = new();

    private string reason = string.Empty;
    private bool busy;

    private async Task Save()
    {
        busy = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, _, error) = await Http.ExecuteRequestAsync<ApiResponse<WalletFundingDto>>(
                HttpVerb.Post, $"api/petty-cash/fundings/{Funding.Id}/reject", new { Reason = reason });

            if (!ok)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to reject the funding.", Severity.Error);
                return;
            }

            Snackbar.Add("Funding rejected.", Severity.Success);
            SideDialog.Close(true);
        }
        finally
        {
            busy = false;
        }
    }
}
