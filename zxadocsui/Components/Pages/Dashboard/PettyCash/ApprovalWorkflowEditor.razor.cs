using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocsui.Srevices;

namespace zxadocsui.Components.Pages.Dashboard.PettyCash;

/// <summary>Builds a workflow's levels in the side panel; closes with true when it saved. The API validates coverage and roles.</summary>
public partial class ApprovalWorkflowEditor
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    [Parameter] public ApprovalWorkflowDto Workflow { get; set; } = new();

    private List<ListOption> roles = new();
    private bool busy;

    protected override async Task OnInitializedAsync()
    {
        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (ok, result, _) = await Http.GetAsync<ApiResponse<List<ListOption>>>("api/petty-cash/workflows/approver-roles");
        roles = ok && result?.Data is not null ? result.Data : new();
    }

    private async Task Save()
    {
        busy = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, _, error) = Workflow.Id == 0
                ? await Http.ExecuteRequestAsync<ApiResponse<ApprovalWorkflowDto>>(HttpVerb.Post, "api/petty-cash/workflows", Workflow)
                : await Http.ExecuteRequestAsync<ApiResponse<ApprovalWorkflowDto>>(HttpVerb.Put, $"api/petty-cash/workflows/{Workflow.Id}", Workflow);

            if (!ok)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to save the workflow.", Severity.Error);
                return;
            }

            Snackbar.Add("Workflow saved.", Severity.Success);
            SideDialog.Close(true);
        }
        finally
        {
            busy = false;
        }
    }
}
