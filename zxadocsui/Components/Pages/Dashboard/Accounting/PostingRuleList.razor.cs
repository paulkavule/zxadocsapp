using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.Srevices;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Accounting;

/// <summary>Posting rules (ZD-197), current and ended. ManageAccounting adds rules; ViewPettyCashFinance reads.</summary>
public partial class PostingRuleList
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    private List<PostingRuleDto> rules = new();
    private bool canEdit;
    private bool loading = true;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        if (!await Permissions.HasAny(Permission.ManageAccounting, Permission.ViewPettyCashFinance))
        {
            Snackbar.Add("You do not have access to posting rules.", Severity.Warning);
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
            var (ok, result, error) = await Http.GetAsync<ApiResponse<List<PostingRuleDto>>>("api/accounting/posting-rules");
            if (!ok || result?.Data is null)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to load posting rules.", Severity.Error);
                return;
            }
            rules = result.Data;
        }
        finally
        {
            loading = false;
        }
    }

    private async Task OpenEditor()
    {
        var saved = await SideDialog.Show<PostingRuleEditor, bool?>(new Dictionary<string, object>(), title: "New posting rule");
        if (saved == true)
        {
            await Load();
            StateHasChanged();
        }
    }
}
