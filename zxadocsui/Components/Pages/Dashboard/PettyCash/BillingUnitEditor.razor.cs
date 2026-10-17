using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocsui.Srevices;

namespace zxadocsui.Components.Pages.Dashboard.PettyCash;

/// <summary>Adds or edits a billing unit in the side panel; closes with true when it saved. The API validates.</summary>
public partial class BillingUnitEditor
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    [Parameter] public BillingUnitDto Unit { get; set; } = new();

    private List<ListOption> departments = new();
    private List<ApprovalWorkflowDto> workflows = new();
    private bool busy;

    protected override async Task OnInitializedAsync()
    {
        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (ok, result, _) = await Http.GetAsync<ApiResponse<List<ListOption>>>("api/departments");
        departments = ok && result?.Data is not null ? result.Data : new();
        var (workflowsOk, workflowList, _) = await Http.GetAsync<ApiResponse<List<ApprovalWorkflowDto>>>("api/petty-cash/workflows");
        workflows = workflowsOk && workflowList?.Data is not null ? workflowList.Data : new();
    }

    private async Task Save()
    {
        busy = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, _, error) = Unit.Id == 0
                ? await Http.ExecuteRequestAsync<ApiResponse<BillingUnitDto>>(HttpVerb.Post, "api/petty-cash/billing-units", Unit)
                : await Http.ExecuteRequestAsync<ApiResponse<BillingUnitDto>>(HttpVerb.Put, $"api/petty-cash/billing-units/{Unit.Id}", Unit);

            if (!ok)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to save the billing unit.", Severity.Error);
                return;
            }

            Snackbar.Add(Unit.Id == 0 ? "Billing unit added." : "Billing unit updated.", Severity.Success);
            SideDialog.Close(true);
        }
        finally
        {
            busy = false;
        }
    }
}
