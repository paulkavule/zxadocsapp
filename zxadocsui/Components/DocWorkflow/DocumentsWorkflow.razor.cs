using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Dtos;
using zxadocsfe.Services;
using zxadocslib.Dtos;

namespace zxadocsui.Components.DocWorkflow;

public partial class DocumentsWorkflow
{

    [Inject] ISnackbar? Snackbar { get; set; } = default;
    [Inject] ILogger<DocumentsWorkflow>? logger { set; get; }
    [Inject] private IHttpService httpSvc { get; set; } = default!;
    List<DocsWorkflow> workflowList = new(), usersList = new();
    [Parameter] public Document? Document { set; get; }
    DocsWorkflow nextActor = new();
    private async Task OnForwardToChanged(DocsWorkflow option)
    {
        if (option == null || option.ActorId == 0)
            return;
        workflowList?.Add(new DocsWorkflow { ActorId = option.ActorId, DocumentId = option.DocumentId, IsFinal = option.IsFinal, Id = option.Id, Level = workflowList?.Count + 1 ?? 1, Name = option.Name });
    }


    private async Task<IEnumerable<DocsWorkflow>> SearchCountries(string value, CancellationToken token)
    {
        try
        {
            if (string.IsNullOrEmpty(value) || value.Length < 3)
                return usersList.AsEnumerable();

            var (_, result, _) = await httpSvc!.GetAsync<ApiResponse<List<ListOption>>>($"api/listoptions/1?type=usersearch&category={value}");

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
        var count = 1;
        workflowList.ForEach((wf) =>
        {
            wf.Level = count;
            count++;
        });
    }

    private void MoveDown(DocsWorkflow item)
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
    }
    void DeleteItem(DocsWorkflow item) => workflowList.Remove(item);
    void AddActor()
    {
        workflowList.Add(nextActor);
    }
    public List<DocsWorkflow> WorflowList()
    {
        workflowList.Last()?.IsFinal = true;
        return workflowList;
    }

}