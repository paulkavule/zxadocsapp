using System;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Dtos;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard;

public partial class Documents
{
    [Inject] ILogger<CreateWorkflow>? Logger { get; set; } = default!;
    [Inject] IHttpService? HttpSvc { get; set; } = default!;
    [Inject] IDialogService? DialogService { get; set; }
    [Inject] ISnackbar? Snackbar { get; set; }
    [Inject] IUserSession? Session { get; set; }

    [Inject] NavigationManager Navigator { set; get; } = default!;
    List<QueryDto.DocumentQuery> inboxList = new(), outboxList = new(), archievedList = new(), deletedList = new(), draftList = new();

    UserData userData = new();
    protected override Task OnInitializedAsync()
    {
        return base.OnInitializedAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            userData = await Session!.GetCurrentUser();
            HttpSvc?.Initialize(AppConstants.HttpSchemes.Core);
            await LoadDocuments(DocStatus.Published);
            StateHasChanged();
        }
    }
    private async Task CreateDocument()
    {
        Navigator.NavigateTo("/newdocument");
    }

    private async Task LoadDocuments(DocStatus folder)
    {
        try
        {
            var (status, result, message) = await HttpSvc!.GetAsync<ApiResponse<List<QueryDto.DocumentQuery>>>($"/api/documents/dashboard?id={(int)folder}&userName={userData.UserId}&pageNumber=1&pageSize=100");
            if (status == false)
                return;
            if (!status || result == null || result.Data.Count <= 0)
                return;
            switch (folder)
            {
                case DocStatus.Published:
                    inboxList = result.Data.Where(doc => doc.NextActorId == int.Parse(userData.UserId) && doc.Status != DocStatus.Archived).ToList();
                    break;
                case DocStatus.Archived:
                    archievedList = result.Data.Where(doc => doc.NextActorId != int.Parse(userData.UserId) && doc.Status == DocStatus.Archived).ToList();
                    break;
                case DocStatus.Outbox:
                    outboxList = result.Data.Where(doc => doc.Author.Id == int.Parse(userData.UserId)).ToList();
                    break;
                case DocStatus.Deleted:
                    deletedList = result.Data.Where(doc => doc.Status == DocStatus.Deleted).ToList();
                    break;
                case DocStatus.Draft:
                    draftList = result.Data.Where(doc => doc.Status == DocStatus.Draft).ToList();
                    break;
            }

            // outboxList = result.Data.Where(doc => doc.Status == (int)DocStatus.Outbox).ToList();

            // deletedList = result.Data.Where(doc => doc.Status == (int)DocStatus.Deleted).ToList();

            Console.WriteLine("Access Token: " + result.Data.Count);
        }
        catch (Exception ee)
        {
            Snackbar!.Clear();
            Snackbar.Add($"Faile to load documents {ee.Message}", Severity.Error);
        }
    }

    async Task IndexChanged(int page)
    {

        DocStatus folder = DocStatus.Published;
        if (page == 1)
            folder = DocStatus.Outbox;
        else if (page == 2)
            folder = DocStatus.Archived;
        else if (page == 3)
            folder = DocStatus.Deleted;

        await LoadDocuments(folder);

    }
}
