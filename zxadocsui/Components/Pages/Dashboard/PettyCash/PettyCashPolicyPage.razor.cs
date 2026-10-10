using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.PettyCash;

/// <summary>
/// The organisation's petty cash policy (ZD-172). FinanceAdmin edits it and sees its history;
/// ViewPettyCashFinance reads it. Each save is a new version, validated by the API.
/// </summary>
public partial class PettyCashPolicyPage
{
    private static readonly string[] Methods = ["MTN", "AIRTEL", "UTILITY"];

    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private PettyCashPolicy policy = new();
    private List<PettyCashPolicy> history = new();
    private bool canEdit;
    private bool loading = true;
    private bool saving;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        if (!await Permissions.HasAny(Permission.FinanceAdmin, Permission.ViewPettyCashFinance))
        {
            Snackbar.Add("You do not have access to the petty cash policy.", Severity.Warning);
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
            var (ok, result, error) = await Http.GetAsync<ApiResponse<PettyCashPolicy>>("api/petty-cash/policy");
            if (!ok || result?.Data is null)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to load the policy.", Severity.Error);
                return;
            }
            policy = result.Data;

            if (canEdit)
            {
                var (historyOk, versions, _) = await Http.GetAsync<ApiResponse<List<PettyCashPolicy>>>("api/petty-cash/policy/history");
                history = historyOk && versions?.Data is not null ? versions.Data : new();
            }
        }
        finally
        {
            loading = false;
        }
    }

    private void Toggle(string method, bool on) =>
        policy.AllowedPaymentMethods = on
            ? policy.AllowedPaymentMethods.Append(method).Distinct().ToArray()
            : policy.AllowedPaymentMethods.Where(m => m != method).ToArray();

    private async Task Save()
    {
        saving = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, _, error) = await Http.ExecuteRequestAsync<ApiResponse<PettyCashPolicy>>(
                HttpVerb.Put, "api/petty-cash/policy", policy);

            Snackbar.Add(ok ? "Policy saved as a new version." : ErrorMessage.Extract(error) ?? "Failed to save the policy.",
                ok ? Severity.Success : Severity.Error);
            if (ok) await Load();
        }
        finally
        {
            saving = false;
        }
    }

    private static string Money(decimal? amount) => amount is null ? "none" : amount.Value.ToString("N0");
}
