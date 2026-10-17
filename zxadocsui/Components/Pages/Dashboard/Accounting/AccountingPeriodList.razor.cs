using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.Srevices;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Accounting;

/// <summary>Accounting periods (ZD-175). ManageAccounting adds, closes and reopens them; ViewPettyCashFinance reads.</summary>
public partial class AccountingPeriodList
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    private List<AccountingPeriodDto> periods = new();
    private bool canEdit;
    private bool loading = true;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        if (!await Permissions.HasAny(Permission.ManageAccounting, Permission.ViewPettyCashFinance))
        {
            Snackbar.Add("You do not have access to accounting periods.", Severity.Warning);
            Nav.NavigateTo("/dashboard");
            return;
        }

        canEdit = await Permissions.Has(Permission.ManageAccounting);
        await Load();
        StateHasChanged();
    }

    private async Task Load()
    {
        loading = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, result, error) = await Http.GetAsync<ApiResponse<List<AccountingPeriodDto>>>("api/accounting/periods");
            if (!ok || result?.Data is null)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to load periods.", Severity.Error);
                return;
            }
            periods = result.Data;
        }
        finally
        {
            loading = false;
        }
    }

    private async Task OpenEditor()
    {
        var saved = await SideDialog.Show<AccountingPeriodEditor, bool?>(new Dictionary<string, object>(), title: "New period");
        if (saved == true)
        {
            await Load();
            StateHasChanged();
        }
    }

    private async Task SetOpen(AccountingPeriodDto period, bool isOpen)
    {
        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (ok, _, error) = await Http.ExecuteRequestAsync<ApiResponse<AccountingPeriodDto>>(
            HttpVerb.Patch, $"api/accounting/periods/{period.Id}/status", new { IsOpen = isOpen });

        Snackbar.Add(ok ? $"{period.Name} {(isOpen ? "reopened" : "closed")}." : ErrorMessage.Extract(error) ?? "Failed to change the period.",
            ok ? Severity.Success : Severity.Error);
        await Load();
    }
}
