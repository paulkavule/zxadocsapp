using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.PettyCash;

/// <summary>
/// A request for review (ZD-310): details, provider check, attachments, wallet position, warnings and
/// history. Actions appear only when the API says the caller may act (ZD-211).
/// </summary>
public partial class RequestDetail
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    [Parameter] public int Id { get; set; }

    private RequestReviewDto? review;
    private string comment = string.Empty;
    private bool busy;
    private bool canRetryAccounting;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        await Session.GetCurrentUser();
        canRetryAccounting = await Permissions.HasAny(Permission.ManageAccounting, Permission.ManagePettyCashOperations);
        await Load();
        StateHasChanged();
    }

    private async Task Load()
    {
        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (ok, result, error) = await Http.GetAsync<ApiResponse<RequestReviewDto>>($"api/petty-cash/requests/{Id}/review");
        if (!ok || result?.Data is null)
        {
            Snackbar.Add(ErrorMessage.Extract(error) ?? "Request not found.", Severity.Error);
            Nav.NavigateTo("/petty-cash/requests");
            return;
        }
        review = result.Data;
    }

    private Task Approve() => Decide("approve", "Approved.");

    private Task RetryAccounting() => Decide("retry-accounting", "Sent back to accounting.");

    private Task Return() => Decide("return", "Returned to the maker.");

    private Task Reject() => Decide("reject", "Rejected.");

    private async Task Decide(string action, string done)
    {
        busy = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, _, error) = await Http.ExecuteRequestAsync<ApiResponse<PettyCashRequestDto>>(
                HttpVerb.Post, $"api/petty-cash/requests/{Id}/{action}", new { Comment = comment });
            Snackbar.Add(ok ? done : ErrorMessage.Extract(error) ?? "The decision could not be saved.", ok ? Severity.Success : Severity.Error);
            if (ok) comment = string.Empty;
            await Load();
        }
        finally
        {
            busy = false;
        }
    }

    private string StatusLine() => review?.Request is not { } r ? "" : r.Status switch
    {
        RequestStatus.UnderApproval => $"awaiting approval at level {r.CurrentLevel}",
        RequestStatus.Approved => $"approved; accounting {r.AccountingStatus.ToString().ToLowerInvariant()}",
        _ => r.Status.ToString().ToLowerInvariant(),
    };
}
