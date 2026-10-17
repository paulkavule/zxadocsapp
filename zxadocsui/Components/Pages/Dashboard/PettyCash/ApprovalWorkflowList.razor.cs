using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.Srevices;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.PettyCash;

/// <summary>Approval workflows (ZD-274). FinanceAdmin builds and versions them; ViewPettyCashFinance reads.</summary>
public partial class ApprovalWorkflowList
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    private List<ApprovalWorkflowDto> workflows = new();
    private bool canEdit;
    private bool loading = true;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        if (!await Permissions.HasAny(Permission.FinanceAdmin, Permission.ViewPettyCashFinance))
        {
            Snackbar.Add("You do not have access to approval workflows.", Severity.Warning);
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
            var (ok, result, error) = await Http.GetAsync<ApiResponse<List<ApprovalWorkflowDto>>>("api/petty-cash/workflows");
            if (!ok || result?.Data is null)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to load workflows.", Severity.Error);
                return;
            }
            workflows = result.Data;
        }
        finally
        {
            loading = false;
        }
    }

    private async Task OpenEditor(ApprovalWorkflowDto workflow)
    {
        var copy = workflow with { Levels = workflow.Levels.Select(l => l with { }).ToList() };
        var saved = await SideDialog.Show<ApprovalWorkflowEditor, bool?>(
            new Dictionary<string, object> { { nameof(ApprovalWorkflowEditor.Workflow), copy } },
            title: workflow.Id == 0 ? "New workflow" : $"Edit {workflow.Name}");

        if (saved == true)
        {
            await Load();
            StateHasChanged();
        }
    }

    private static string Band(ApprovalLevelDto level) => (level.MinAmount, level.MaxAmount) switch
    {
        (null, null) => "",
        (var min, null) => $"(from {min:N0})",
        (null, var max) => $"(up to {max:N0})",
        var (min, max) => $"({min:N0} to {max:N0})",
    };
}
