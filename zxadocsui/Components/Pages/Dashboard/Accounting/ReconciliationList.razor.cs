using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Accounting;

/// <summary>Reconciliation exceptions with filters (ZD-303); ManageAccounting holders can reconcile on demand (ZD-281).</summary>
public partial class ReconciliationList
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private List<ReconciliationExceptionDto> items = new();
    private List<BillingUnitDto> units = new();
    private ReconciliationExceptionStatus? status = ReconciliationExceptionStatus.Open;
    private ReconciliationExceptionType? type;
    private int? unitId;
    private PaymentType? provider;
    private DateTime? from;
    private DateTime? to;
    private bool canRun;
    private bool running;
    private bool loading = true;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        if (!await Permissions.HasAny(Permission.ManageAccounting, Permission.ViewPettyCashFinance))
        {
            Snackbar.Add("You do not have access to reconciliation.", Severity.Warning);
            Nav.NavigateTo("/dashboard");
            return;
        }

        canRun = await Permissions.Has(Permission.ManageAccounting);
        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (unitsOk, unitList, _) = await Http.GetAsync<ApiResponse<List<BillingUnitDto>>>("api/petty-cash/billing-units");
        units = unitsOk && unitList?.Data is not null ? unitList.Data : new();
        await Load();
        StateHasChanged();
    }

    private async Task Load()
    {
        loading = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, result, error) = await Http.GetAsync<ApiResponse<List<ReconciliationExceptionDto>>>(ListUrl);
            if (!ok || result?.Data is null)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to load exceptions.", Severity.Error);
                return;
            }
            items = result.Data;
        }
        finally
        {
            loading = false;
        }
    }

    /// <summary>The list endpoint with the filters on screen; the export sends the same (ZD-230).</summary>
    private string ListUrl
    {
        get
        {
            var query = new List<string>();
            if (status is not null) query.Add($"status={status}");
            if (type is not null) query.Add($"type={type}");
            if (unitId is not null) query.Add($"billingUnitId={unitId}");
            if (provider is not null) query.Add($"provider={provider}");
            if (from is not null) query.Add($"from={from:yyyy-MM-dd}");
            if (to is not null) query.Add($"to={to.Value.AddDays(1):yyyy-MM-dd}");
            return "api/accounting/reconciliation/exceptions" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
        }
    }

    private async Task RunNow()
    {
        running = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, result, error) = await Http.ExecuteRequestAsync<ApiResponse<ReconciliationRunDto>>(
                HttpVerb.Post, "api/accounting/reconciliation/runs", new { });
            if (!ok || result?.Data is null)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Reconciliation failed.", Severity.Error);
                return;
            }

            var run = result.Data;
            Snackbar.Add(run.Error.Length > 0
                    ? $"Stopped after {run.Checked} requests: {run.Error}"
                    : $"Checked {run.Checked}: {run.Reconciled} reconciled, {run.Exceptions} with exceptions.",
                run.Error.Length > 0 ? Severity.Warning : Severity.Success);
            await Load();
        }
        finally
        {
            running = false;
        }
    }

    private static string Age(DateTime since)
    {
        var age = DateTime.UtcNow - since;
        return age.TotalDays >= 1 ? $"{(int)age.TotalDays} d" : age.TotalHours >= 1 ? $"{(int)age.TotalHours} h" : $"{(int)age.TotalMinutes} min";
    }

    internal static string TypeName(ReconciliationExceptionType type) => type switch
    {
        ReconciliationExceptionType.MissingAccounting => "Missing accounting",
        ReconciliationExceptionType.MissingProviderTransaction => "Missing provider payment",
        ReconciliationExceptionType.AmountMismatch => "Amount mismatch",
        ReconciliationExceptionType.Duplicate => "Duplicate",
        _ => "Status mismatch",
    };

    internal static string ProviderName(PaymentType type) => type switch
    {
        PaymentType.MtnMobileMoney => "MTN",
        PaymentType.AirtelMoney => "Airtel",
        _ => "Utility",
    };
}
