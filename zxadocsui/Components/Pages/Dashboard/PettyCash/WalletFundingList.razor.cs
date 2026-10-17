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
/// Wallet funding (ZD-291). FinanceAdmin initiates; ApprovePettyCashRequest holders approve or reject,
/// and the API refuses the initiator. ViewPettyCashFinance reads.
/// </summary>
public partial class WalletFundingList
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;
    [Inject] private IDialogService Dialogs { get; set; } = default!;

    private List<WalletFundingDto> fundings = new();
    private bool canInitiate;
    private bool canApprove;
    private bool loading = true;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        if (!await Permissions.HasAny(Permission.FinanceAdmin, Permission.ApprovePettyCashRequest, Permission.ViewPettyCashFinance))
        {
            Snackbar.Add("You do not have access to wallet funding.", Severity.Warning);
            Nav.NavigateTo("/dashboard");
            return;
        }

        canInitiate = await Permissions.Has(Permission.FinanceAdmin);
        canApprove = await Permissions.Has(Permission.ApprovePettyCashRequest);
        await Load();
        StateHasChanged();
    }

    private async Task Load()
    {
        loading = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, result, error) = await Http.GetAsync<ApiResponse<List<WalletFundingDto>>>("api/petty-cash/fundings");
            if (!ok || result?.Data is null)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to load fundings.", Severity.Error);
                return;
            }
            fundings = result.Data;
        }
        finally
        {
            loading = false;
        }
    }

    private async Task OpenEditor()
    {
        var saved = await SideDialog.Show<WalletFundingEditor, bool?>(new Dictionary<string, object>(), title: "New wallet funding");
        if (saved == true)
        {
            await Load();
            StateHasChanged();
        }
    }

    private async Task Approve(WalletFundingDto funding)
    {
        var confirmed = await Dialogs.ShowMessageBoxAsync("Approve funding",
            $"Post {funding.Amount:N0} UGX into {funding.BillingUnitCode} (reference {funding.Reference})?", yesText: "Approve", cancelText: "Cancel");
        if (confirmed != true) return;

        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (ok, result, error) = await Http.ExecuteRequestAsync<ApiResponse<WalletFundingDto>>(
            HttpVerb.Post, $"api/petty-cash/fundings/{funding.Id}/approve", new { });
        Snackbar.Add(ok ? $"Approved; posted {result?.Data?.JournalNumber}." : ErrorMessage.Extract(error) ?? "Failed to approve.",
            ok ? Severity.Success : Severity.Error);
        await Load();
        StateHasChanged();
    }

    private async Task Reject(WalletFundingDto funding)
    {
        var decided = await SideDialog.Show<WalletFundingRejectEditor, bool?>(
            new Dictionary<string, object> { { nameof(WalletFundingRejectEditor.Funding), funding } },
            title: $"Reject funding {funding.Reference}");
        if (decided == true)
        {
            await Load();
            StateHasChanged();
        }
    }

    private static Color StatusColor(FundingStatus status) => status switch
    {
        FundingStatus.Approved => Color.Success,
        FundingStatus.Rejected => Color.Error,
        _ => Color.Warning,
    };
}
