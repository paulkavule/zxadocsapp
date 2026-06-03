using System;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Dtos;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.Srevices;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard;

public partial class CreateDocumentWorkflow
{
    [Inject] ISnackbar Snackbar { set; get; } = default!;
    [Inject] ILogger<CreateDocumentWorkflow> logger { set; get; } = default!;
    [Inject] IDialogService dialog { set; get; } = default!;
    [Inject] SideDialogService sideDialog { get; set; } = default!;
    [Inject] IUserSession? session { set; get; }
    [Inject] IHttpService httpSvc { get; set; } = default!;
    private List<ListOption> doctypeList = new(), docCatList = new();
    List<WorkFlow> workflowList = new();
    string docCategory = "", userid = "";
    bool rearranged = false, updating, saving;
    int orgId = 0;
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            if (firstRender)
            {
                var userData = await session!.GetCurrentUser();
                httpSvc.Initialize(AppConstants.HttpSchemes.Core);
                int.TryParse(userData.OrgId, out orgId);
                string _userId = userData.UserId ?? "";
                userid = DataEncryptor.Decrypt(_userId);
                await loadDocumentTypes();
            }

        }
        catch (Exception ee)
        {
            logger!.LogError(ee, ee.Message);
        }
    }
    private async Task loadDocumentTypes()
    {
        try
        {
            var (status, result, message) = await httpSvc!.GetAsync<ApiResponse<List<ListOption>>>("api/listoptions/1?type=documenttype");
            if (status == false || result?.Data == null) return;

            doctypeList = result.Data;
            doctypeList.Insert(0, new ListOption { Id = 0, Name = "Select" });
        }
        catch (Exception ex)
        {
            logger!.LogDebug(ex.Message);
        }
    }

    private async Task DocTypeChanged(string value)
    {
        try
        {
            if (value == "0")
                return;
            docCatList.Clear();

            var (status, result, message) = await httpSvc!.GetAsync<ApiResponse<List<ListOption>>>($"api/listoptions/1?type=documentcategory&category={value}");
            if (status == false || result?.Data == null)
            {
                //show dialog at this point
                Snackbar!.Add(message!, Severity.Error);
                return;
            }

            docCatList = result.Data;
            docCatList.Insert(0, new ListOption { Id = 0, Name = "Select" });
        }
        catch (Exception ex)
        {
            logger!.LogDebug(ex.Message);
        }
    }
    async Task DocCatChanged(string value)
    {
        docCategory = value;
        var (status, result, message) = await httpSvc!.GetAsync<ApiResponse<List<WorkFlow>>>($"api/doccategoryworkflow/category/{docCategory}");
        if (status == false || result?.Data == null)
        {
            //show dialog at this point
            Snackbar!.Add(message!, Severity.Error);
            return;
        }

        workflowList = result.Data;
    }

    private void MoveUp(WorkFlow item)
    {
        var index = workflowList.IndexOf(item);
        if (index <= 0) return;

        (workflowList[index - 1], workflowList[index]) =
            (workflowList[index], workflowList[index - 1]);
        var count = 1;
        workflowList.ForEach((wf) =>
        {
            wf.Level = count;
            count++;
        });

        rearranged = true;
    }


    private void MoveDown(WorkFlow item)
    {
        var index = workflowList.IndexOf(item);
        if (index < 0 || index >= workflowList.Count - 1) return;

        (workflowList[index + 1], workflowList[index]) =
            (workflowList[index], workflowList[index + 1]);
        var count = 1;
        workflowList.ForEach((wf) =>
        {
            wf.Level = count;
            count++;
        });
        rearranged = true;
    }
    void DeleteItem(WorkFlow item) => workflowList.Remove(item);

    async Task DeleteCategory()
    {

    }
    async Task SaveLevelChanges()
    {
        Snackbar.Clear();
        try
        {
            updating = true;

            var (proceed, _, message) = await httpSvc.ExecuteRequestAsync<ApiResponse<object>>(HttpVerb.Patch, $"/api/doccategoryworkflow/category/{docCategory}/levels", workflowList);

            if (proceed == false)
            {
                Snackbar.Add(string.IsNullOrEmpty(message) ? "Couldn't save the changes" : message, Severity.Error);
                return;
            }
            Snackbar.Add("Workflow has been successfully saved", Severity.Success);
        }
        catch (Exception ee)
        {
            Snackbar.Add(ee.Message, Severity.Error);
            logger.LogError(ee, ee.Message);
        }
        finally
        {
            updating = false;
        }
    }
    async Task AddStepToCategory()
    {
        var result = await sideDialog.Show<EditDocWorkflow, WorkFlow>(title: "Choose role");
        Snackbar.Clear();
        if (result == null)
            return;
        result.Level = workflowList.Count() + 1;
        workflowList.Add(result);

        var data = new zxadocslib.Dtos.WorkFlow
        {
            IsRequired = result.IsRequired,
            RoleId = result.RoleId,
            Level = workflowList.Count,
            OrganisationId = orgId,
            CategoryId = int.Parse(docCategory),
            CreatedBy = int.Parse(userid),

        };
        var (proceed, _, message) = await httpSvc.ExecuteRequestAsync<ApiResponse<object>>(HttpVerb.Post, "api/doccategoryworkflow", data);

        Snackbar!.Clear();
        Snackbar!.Add(proceed ? "Success" : message ?? "Something went wrong", proceed ? Severity.Success : Severity.Error);
    }

    async Task EditDocWorkflow(WorkFlow flow)
    {
        var result = await sideDialog.Show<EditDocWorkflow, WorkFlow>(new Dictionary<string, object> { { "Work", flow } }, title: "Choose role");

        if (result == null)
            return;
        result.Level = workflowList.Count() + 1;
        workflowList.Add(result);
        Snackbar.Clear();
        var data = new zxadocslib.Dtos.WorkFlow
        {
            IsRequired = result.IsRequired,
            RoleId = result.RoleId,
            Level = workflowList.Count,
            OrganisationId = orgId,
            CategoryId = int.Parse(docCategory),
            CreatedBy = int.Parse(userid),

        };
        var (proceed, _, message) = await httpSvc.ExecuteRequestAsync<ApiResponse<object>>(HttpVerb.Post, "api/doccategoryworkflow", data);

        Snackbar!.Clear();
        Snackbar!.Add(proceed ? "Success" : message ?? "Something went wrong", proceed ? Severity.Success : Severity.Error);

    }
}
