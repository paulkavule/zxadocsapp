using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.Srevices;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Accounting;

/// <summary>The chart of accounts (ZD-155). ManageAccounting edits; ViewPettyCashFinance reads.</summary>
public partial class GlAccountList
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;
    [Inject] private IDialogService Dialogs { get; set; } = default!;

    private List<GlAccountDto> accounts = new();
    private bool canEdit;
    private bool loading = true;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        if (!await Permissions.HasAny(Permission.ManageAccounting, Permission.ViewPettyCashFinance))
        {
            Snackbar.Add("You do not have access to the chart of accounts.", Severity.Warning);
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
            var (ok, result, error) = await Http.GetAsync<ApiResponse<List<GlAccountDto>>>("api/accounting/accounts");
            if (!ok || result?.Data is null)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to load accounts.", Severity.Error);
                return;
            }
            accounts = result.Data;
        }
        finally
        {
            loading = false;
        }
    }

    private string ParentCode(int? parentId) => accounts.FirstOrDefault(a => a.Id == parentId)?.Code ?? "";

    private async Task OpenEditor(GlAccountDto account)
    {
        var saved = await SideDialog.Show<GlAccountEditor, bool?>(
            new Dictionary<string, object>
            {
                { nameof(GlAccountEditor.Account), account with { } },
                { nameof(GlAccountEditor.Parents), accounts.Where(a => a.Id != account.Id).ToList() },
            },
            title: account.Id == 0 ? "New account" : $"Edit {account.Code}");

        if (saved == true)
        {
            await Load();
            StateHasChanged();
        }
    }

    private async Task ConfirmDelete(GlAccountDto account)
    {
        var confirmed = await Dialogs.ShowMessageBoxAsync(
            "Delete account", $"Delete {account.Code} {account.Name}?", yesText: "Delete", cancelText: "Cancel");
        if (confirmed != true) return;

        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (ok, _, error) = await Http.ExecuteRequestAsync<ApiResponse<int>>(HttpVerb.Delete, $"api/accounting/accounts/{account.Id}");
        // Referenced accounts are refused by the API; deactivating is the alternative.
        Snackbar.Add(ok ? "Account deleted." : ErrorMessage.Extract(error) ?? "Failed to delete.", ok ? Severity.Success : Severity.Error);
        if (ok) await Load();
        StateHasChanged();
    }
}
