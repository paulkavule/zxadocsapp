using System;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard;

public partial class DocumentFolders
{
    [Inject] public IHttpService? httpSvc { set; get; }
    [Inject] public ILogger<DocumentFolders>? logger { set; get; }
    [Inject] public ISnackbar? Snackbar { set; get; }
    [Inject] public IUserSession session { set; get; } = default!;

    // The list-options endpoint reads the organisation from the route and never falls back to the
    // token, so a hardcoded id here serves org 1's values to every tenant.
    private int orgId;

    bool success;
    string[] errors = { };
    Document document = new();

    private List<ListOption> priorityList = new(), doctypeList = new(), docCatList = new(), usersList = new();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var userData = await session.GetCurrentUser();
            int.TryParse(userData.OrgId, out orgId);

            await loadPriorities();
            await loadDocumentTypes();

        }
    }
    private async Task loadPriorities()
    {
        try
        {
            var (status, result, message) = await
httpSvc!.GetAsync<ApiResponse<List<ListOption>>>($"api/listoptions/{orgId}?type=Priority");
            if (status == false || result?.Data == null)
            {
                //show dialog at this point
                Snackbar?.Clear();
                Snackbar?.Add(message!, Severity.Normal);
                return;
            }

            priorityList = result.Data;
        }
        catch (Exception ex)
        {
            logger!.LogDebug(ex.Message);
        }
    }

    private async Task loadDocumentTypes()
    {
        try
        {
            var (status, result, message) = await
httpSvc!.GetAsync<ApiResponse<List<ListOption>>>($"api/listoptions/{orgId}?type=documenttype");
            if (status == false || result?.Data == null)
            {
                //show dialog at this point
                return;
            }

            doctypeList = result.Data;
        }
        catch (Exception ex)
        {
            logger!.LogDebug(ex.Message);
        }
    }

    public void SubmitDocument()
    {

    }
}
