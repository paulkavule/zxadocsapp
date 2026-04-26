using System;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;

namespace zxadocsui.Components.Pages.Dashboard;

public partial class Documents
{
    [Inject] ILogger<CreateWorkflow>? Logger { get; set; } = default!;
    [Inject] IHttpService? HttpSvc { get; set; } = default!;
    [Inject] IDialogService? DialogService { get; set; }
    [Inject] ISnackbar? Snackbar { get; set; }
    [Inject] NavigationManager Navigator { set; get; } = default!;
    List<QueryDto.DocumentQuery> inboxList = new(), outboxList = new(), archievedList = new(), deletedList = new();
    string userName = "pkavule";
    protected override Task OnInitializedAsync()
    {
        return base.OnInitializedAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            HttpSvc?.Initialize(AppConstants.HttpSchemes.Core);
            await LoadDocuments(0);

            StateHasChanged();
        }
    }
    private async Task CreateDocument()
    {
        Navigator.NavigateTo("/newdocument");
    }

    private async Task LoadDocuments(int inboxId)
    {
        var (status, result, message) = await HttpSvc!.GetAsync<ApiResponse<List<QueryDto.DocumentQuery>>>($"/api/documents/dashboard/{inboxId}?userName={userName}&pageNumber=1&pageSize=100");
        if (status == false)
            return;
        if (!status || result == null || result.Data.Count <= 0)
            return;

        switch (inboxId)
        {
            case 0:
                inboxList = result.Data;
                break;

            case 1:
                outboxList = result.Data;
                break;
            case 2:
                archievedList = result.Data;
                break;
            case 3:
                deletedList = result.Data;
                break;
        }
        Console.WriteLine("Access Token: " + result.Data.Count);
    }

    async Task IndexChanged(int page) => await LoadDocuments(page);
}
