using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocsui.Srevices;

namespace zxadocsui.Components.Pages.Dashboard.PettyCash;

/// <summary>Assigns a user to a wallet (ZD-234) in the side panel; closes with true when it saved.</summary>
public partial class WalletAssignEditor
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    [Parameter] public WalletDto Wallet { get; set; } = new();

    private List<ListOption> users = new();
    private int userId;
    private bool busy;

    protected override async Task OnInitializedAsync()
    {
        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (ok, result, _) = await Http.GetAsync<ApiResponse<List<ListOption>>>("api/petty-cash/wallet-assignments/candidates");
        users = ok && result?.Data is not null ? result.Data : new();
    }

    private async Task Save()
    {
        busy = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, _, error) = await Http.ExecuteRequestAsync<ApiResponse<WalletAssignmentDto>>(
                HttpVerb.Post, $"api/petty-cash/wallets/{Wallet.Id}/assignments", new { UserId = userId });

            if (!ok)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to assign the user.", Severity.Error);
                return;
            }

            Snackbar.Add("User assigned.", Severity.Success);
            SideDialog.Close(true);
        }
        finally
        {
            busy = false;
        }
    }
}
