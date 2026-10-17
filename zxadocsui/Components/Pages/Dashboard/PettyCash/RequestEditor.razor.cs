using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.PettyCash;

/// <summary>
/// A maker's draft or returned request (ZD-153). The unit and wallet are the maker's own; the API
/// validates every choice against them and the policy.
/// </summary>
public partial class RequestEditor
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    [Parameter] public int? Id { get; set; }

    private PettyCashRequestDto request = new();
    private WalletDto? wallet;
    private List<SpendingCategoryDto> categories = new();
    private List<UtilityAccountDto> utilityAccounts = new();
    private bool loading = true;
    private bool askReason;
    private string returnComment = string.Empty;
    private string duplicateMessage = string.Empty;
    private string duplicateReason = string.Empty;
    private bool busy;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        if (!await Permissions.Has(Permission.CreatePettyCashRequest))
        {
            Snackbar.Add("You cannot raise petty cash requests.", Severity.Warning);
            Nav.NavigateTo("/dashboard");
            return;
        }

        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (walletOk, myWallet, _) = await Http.GetAsync<ApiResponse<WalletDto>>("api/petty-cash/my-wallet");
            wallet = walletOk ? myWallet?.Data : null;
            var (categoriesOk, usable, _) = await Http.GetAsync<ApiResponse<List<SpendingCategoryDto>>>("api/petty-cash/categories/usable");
            categories = categoriesOk && usable?.Data is not null ? usable.Data : new();
            var (accountsOk, payable, _) = await Http.GetAsync<ApiResponse<List<UtilityAccountDto>>>("api/petty-cash/utility-accounts");
            utilityAccounts = accountsOk && payable?.Data is not null ? payable.Data : new();

            if (Id is { } id)
            {
                var (ok, existing, error) = await Http.GetAsync<ApiResponse<PettyCashRequestDto>>($"api/petty-cash/requests/{id}");
                if (!ok || existing?.Data is null)
                {
                    Snackbar.Add(ErrorMessage.Extract(error) ?? "Request not found.", Severity.Error);
                    Nav.NavigateTo("/petty-cash/requests");
                    return;
                }
                request = existing.Data;
                if (request.Status == RequestStatus.Returned)
                {
                    var (reviewOk, review, _) = await Http.GetAsync<ApiResponse<RequestReviewDto>>($"api/petty-cash/requests/{id}/review");
                    returnComment = reviewOk ? review?.Data?.History.LastOrDefault(h => h.Action == ApprovalAction.Return)?.Comment ?? "" : "";
                }
            }
        }
        finally
        {
            loading = false;
        }
        StateHasChanged();
    }

    /// <summary>Saves first so the server judges what the maker sees, then submits.</summary>
    private async Task Submit()
    {
        if (!await Save()) return;

        busy = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, _, error) = await Http.ExecuteRequestAsync<ApiResponse<PettyCashRequestDto>>(
                HttpVerb.Post, $"api/petty-cash/requests/{request.Id}/submit", new { DuplicateOverrideReason = duplicateReason });
            if (!ok)
            {
                var message = ErrorMessage.Extract(error) ?? "The request could not be submitted.";
                // A possible duplicate can still go ahead with a reason; anything else must be fixed.
                askReason = message.Contains("give a reason");
                duplicateMessage = message;
                Snackbar.Add(message, askReason ? Severity.Warning : Severity.Error);
                return;
            }

            Snackbar.Add($"{request.Reference} submitted for approval.", Severity.Success);
            Nav.NavigateTo($"/petty-cash/requests/{request.Id}");
        }
        finally
        {
            busy = false;
        }
    }

    private async Task Lookup()
    {
        busy = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, result, error) = await Http.ExecuteRequestAsync<ApiResponse<PettyCashRequestDto>>(
                HttpVerb.Post, $"api/petty-cash/requests/{request.Id}/beneficiary-lookup", new { });
            if (!ok || result?.Data is null)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "The lookup could not run.", Severity.Error);
                return;
            }
            request = result.Data;
        }
        finally
        {
            busy = false;
        }
    }

    private async Task<bool> Save()
    {
        busy = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, saved, error) = request.Id == 0
                ? await Http.ExecuteRequestAsync<ApiResponse<PettyCashRequestDto>>(HttpVerb.Post, "api/petty-cash/requests", request)
                : await Http.ExecuteRequestAsync<ApiResponse<PettyCashRequestDto>>(HttpVerb.Put, $"api/petty-cash/requests/{request.Id}", request);

            if (!ok || saved?.Data is null)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to save the request.", Severity.Error);
                return false;
            }

            Snackbar.Add($"Draft {saved.Data.Reference} saved.", Severity.Success);
            if (request.Id == 0)
                Nav.NavigateTo($"/petty-cash/requests/{saved.Data.Id}/edit", replace: true);
            request = saved.Data;
            return true;
        }
        finally
        {
            busy = false;
        }
    }
}
