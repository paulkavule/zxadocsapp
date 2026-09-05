using System.Data.Common;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Dtos;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocsui.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.DocWorkflow;

public partial class DocumentsWorkflow
{

    [Inject] ISnackbar? Snackbar { get; set; } = default;
    [Inject] IUserSession? session { get; set; } = default;
    [Inject] ILogger<DocumentsWorkflow>? logger { set; get; }
    [Inject] private IHttpService httpSvc { get; set; } = default!;
    List<DocsWorkflow> workflowList = new(), usersList = new();
    List<WorkFlow> defaultWorkflow = new();
    [Parameter] public Document? Document { set; get; }

    // MudStep destroys this component when its step is not active, so the chain the author built
    // is handed back by the page on rebuild rather than regenerated from the category defaults.
    [Parameter] public List<DocsWorkflow>? SavedWorkflow { set; get; }
    DocsWorkflow nextActor = new();
    int docCategory = 0, docType = 0;
    string roleId = "";
    int orgId = 0;
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var userData = await session!.GetCurrentUser();
            roleId = userData.RoleId;
            // List options are org-scoped by route, so the search below needs the caller's org.
            int.TryParse(userData.OrgId, out orgId);
        }
        if (docCategory != Document?.CategoryId && Document?.CategoryId > 0)
        {
            // Before any request: both fetches below return early on failure, and the finally then
            // sets docCategory, so a restore left until after them would never run at all.
            var restored = RestoreSavedWorkflow();

            try
            {
                var (status, result, message) = await httpSvc!.GetAsync<ApiResponse<List<WorkFlow>>>($"api/doccategoryworkflow/category/{Document?.CategoryId}");
                if (status == false || result?.Data == null)
                {
                    //show dialog at this point
                    Snackbar!.Add(message!, Severity.Error);
                    return;
                }

                defaultWorkflow = result.Data.OrderBy(dd => dd.Level).ToList();
                Console.WriteLine(" ------- --------  -------- ------- " + defaultWorkflow.Count);

                var rolesIds = string.Join("&", result.Data.Where(it => it.RoleId != int.Parse(roleId)).Select(it => "roleIds=" + it.RoleId));
                (status, var data, message) = await httpSvc!.GetAsync<ApiPaginatedResponse<List<User>>>($"api/users/by-roles?{rolesIds}");
                if (!status || data == null)
                    return;

                usersList = data.Data.Select((dd, i) => new DocsWorkflow
                {
                    ActorId = dd.Id,
                    Name = $"{dd.Name} ({string.Join(",", dd.Roles.Select(rr => rr.RoleName.GetInitials()))})",
                    Level = i + 1,
                    RoleIds = dd.Roles.Select(rr => rr.RoleId).ToList()
                }).ToList();
                if (!restored)
                    RefreshWorkflow();


            }
            catch(Exception ee)
            {
                logger!.LogError(ee, ee.Message);
            }
            finally
            {
                docCategory = Document.CategoryId;
                StateHasChanged();
            }
            // workflowList = result.Data;

        }
    }

    private async Task<IEnumerable<DocsWorkflow>> SearchUsers(string value, CancellationToken token)
    {
        try
        {
            if (string.IsNullOrEmpty(value) || value.Length < 3)
                return usersList.AsEnumerable();

            var (_, result, _) = await httpSvc!.GetAsync<ApiResponse<List<ListOption>>>($"api/listoptions/{orgId}?type=usersearch&category={value}");

            var userList = result?.Data ?? new List<ListOption>();

            return userList.Select(op => new DocsWorkflow { ActorId = op.Id, Name = op.Name });
        }
        catch (Exception ex)
        {
            logger!.LogError(ex, ex.Message);
            return new List<DocsWorkflow>();
        }

    }


    private void MoveUp(DocsWorkflow item)
    {
        var index = workflowList.IndexOf(item);
        if (index <= 0) return;

        (workflowList[index - 1], workflowList[index]) =
            (workflowList[index], workflowList[index - 1]);
        Renumber();
    }

    private void MoveDown(DocsWorkflow item)
    {
        var index = workflowList.IndexOf(item);
        if (index < 0 || index >= workflowList.Count - 1) return;

        (workflowList[index + 1], workflowList[index]) =
            (workflowList[index], workflowList[index + 1]);
        Renumber();
    }

    private async Task ActorSelected(DocsWorkflow option)
    {
        if (option == null || option.ActorId == 0)
            return;
        workflowList?.Add(new DocsWorkflow { ActorId = option.ActorId, DocumentId = option.DocumentId, IsFinal = option.IsFinal, Id = option.Id, Level = workflowList?.Count + 1 ?? 1, Name = option.Name });
    }


    private void Renumber()
    {
        for (var i = 0; i < workflowList.Count; i++)
            workflowList[i].Level = i + 1;
    }

    /// <summary>Removes an actor, unless it is the last one covering a role the default workflow requires.</summary>
    void DeleteItem(DocsWorkflow item)
    {
        // An empty usersList means no default workflow, so no role needs protecting.
        WorkFlow? role = usersList.Count == 0 ? null : defaultWorkflow.FirstOrDefault(rr =>
            item.RoleIds.Contains(rr.RoleId) &&
            !workflowList.Any(wf => !ReferenceEquals(wf, item) && wf.RoleIds.Contains(rr.RoleId)));

        if (role != null)
        {
            Snackbar!.Add($"At least one {role.RoleName} must stay in the workflow", Severity.Warning);
            return;
        }

        workflowList.Remove(item);
        Renumber();
    }

    /// <summary>Repopulates the workflow from the user pool, one copy each so edits leave the pool intact.</summary>
    /// <summary>Puts the author's chain back. False when the page has none, so this is a first visit.</summary>
    bool RestoreSavedWorkflow()
    {
        if (SavedWorkflow is not { Count: > 0 })
            return false;

        workflowList = SavedWorkflow.Select(wf => new DocsWorkflow
        {
            ActorId = wf.ActorId,
            Name = wf.Name,
            Level = wf.Level,
            IsFinal = wf.IsFinal,
            WorkflowType = wf.WorkflowType,
            RoleIds = wf.RoleIds
        }).ToList();
        return true;
    }

    void RefreshWorkflow() =>
        workflowList = usersList.Select((dd, i) => new DocsWorkflow
        {
            ActorId = dd.ActorId,
            Name = dd.Name,
            Level = i + 1,
            RoleIds = dd.RoleIds
        }).ToList();
    void AddActor()
    {
        workflowList.Add(nextActor);
    }
    public List<DocsWorkflow> WorflowList()
    {
        var last = workflowList.LastOrDefault();
        if (last is not null)
            last.IsFinal = true;
        return workflowList;
    }

}