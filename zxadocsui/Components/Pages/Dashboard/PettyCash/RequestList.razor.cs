using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.PettyCash;

/// <summary>Petty cash requests (ZD-153): the caller's own, or the organisation's for finance roles.</summary>
public partial class RequestList
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private readonly List<(string Value, string Label)> scopes = new();
    private List<PettyCashRequestDto> requests = new();
    private string scope = "mine";
    private int userId;
    private bool canCreate;
    private bool loading = true;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        userId = int.TryParse((await Session.GetCurrentUser()).UserId, out var id) ? id : 0;
        canCreate = await Permissions.Has(Permission.CreatePettyCashRequest);
        if (canCreate) scopes.Add(("mine", "My requests"));
        if (await Permissions.Has(Permission.ApprovePettyCashRequest)) scopes.Add(("queue", "Awaiting my approval"));
        if (await Permissions.HasAny(Permission.FinanceAdmin, Permission.ViewPettyCashFinance)) scopes.Add(("all", "All requests"));
        if (scopes.Count == 0)
        {
            Snackbar.Add("You do not have access to petty cash requests.", Severity.Warning);
            Nav.NavigateTo("/dashboard");
            return;
        }

        scope = scopes[0].Value;
        await Load();
        StateHasChanged();
    }

    private async Task Switch(string value)
    {
        scope = value;
        await Load();
    }

    private async Task Load()
    {
        loading = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var url = scope == "queue" ? "api/petty-cash/requests/queue" : $"api/petty-cash/requests?scope={scope}";
            var (ok, result, error) = await Http.GetAsync<ApiResponse<List<PettyCashRequestDto>>>(url);
            if (!ok || result?.Data is null)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to load requests.", Severity.Error);
                return;
            }
            requests = result.Data;
        }
        finally
        {
            loading = false;
        }
    }

    private void Open(PettyCashRequestDto request) =>
        Nav.NavigateTo(request.Status is RequestStatus.Draft or RequestStatus.Returned && request.CreatedById == userId
            ? $"/petty-cash/requests/{request.Id}/edit"
            : $"/petty-cash/requests/{request.Id}");

    private static string StatusText(PettyCashRequestDto request) => (request.Status, request.PaymentStatus) switch
    {
        (RequestStatus.UnderApproval, _) => $"Level {request.CurrentLevel}",
        (RequestStatus.Approved, PaymentStatus.Requested) => "Paying",
        (RequestStatus.Approved, PaymentStatus.Paid) => "Paid",
        (RequestStatus.Approved, PaymentStatus.Failed) => "Payment failed",
        _ => request.Status.ToString(),
    };

    private static Color StatusColor(PettyCashRequestDto request) => request.Status switch
    {
        RequestStatus.Approved when request.PaymentStatus == PaymentStatus.Failed => Color.Error,
        RequestStatus.Approved => Color.Success,
        RequestStatus.Rejected => Color.Error,
        RequestStatus.Returned => Color.Warning,
        RequestStatus.UnderApproval => Color.Info,
        _ => Color.Default,
    };
}
