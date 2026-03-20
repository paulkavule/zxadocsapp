using System;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.State;
using zxadocsfe.Dtos;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;

namespace zxadocsui.Components.DocWorkflow;

public partial class DocumentDetails
{
    [Inject] ISnackbar? Snackbar { get; set; } = default;
    [Inject] ILogger<DocumentDetails>? logger { set; get; }
    [Inject] private IHttpService httpSvc { get; set; } = default!;
    [Parameter] public Document? Document { set; get; }
    // [Parameter] public bool? IsValid { set; get; }
    private List<ListOption> priorityList = new(), doctypeList = new(), docCatList = new(), usersList = new(), workflowList = new();
    private List<DocCategoryField> extraFields = new();
    string[] errors = { };
    public bool success;
    protected override async Task OnInitializedAsync()
    {
        httpSvc.Initialize(AppConstants.HttpSchemes.Core);

        await Task.CompletedTask;
    }
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await loadPriorities();
            await loadDocumentTypes();
            StateHasChanged();
        }
    }

    private async Task loadPriorities()
    {
        try
        {
            var (status, result, message) = await httpSvc!.GetAsync<ApiResponse<List<ListOption>>>("api/listoptions/1?type=Priority");
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
            var (status, result, message) = await httpSvc!.GetAsync<ApiResponse<List<ListOption>>>("api/listoptions/1?type=documenttype");
            if (status == false || result?.Data == null)
            {
                //show dialog at this point
                return;
            }
            doctypeList = result.Data;
            doctypeList.Insert(0, new ListOption { Id = 0, Name = "Select" });
            // Document?.TypeId = 0;
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
            Document.TypeId = int.Parse(value);
            var (status, result, message) = await httpSvc!.GetAsync<ApiResponse<List<ListOption>>>($"api/listoptions/1?type=documentcategory&category={value}");
            if (status == false || result?.Data == null)
            {
                //show dialog at this point
                Snackbar!.Add(message!, Severity.Error);
                return;
            }

            docCatList = result.Data;
        }
        catch (Exception ex)
        {
            logger!.LogDebug(ex.Message);
        }
    }
    private async Task DocCategoryChanged(string value)
    {
        try
        {
            Document.CategoryId = int.Parse(value);
            var (exists, data, message) = await httpSvc!.GetAsync<ApiResponse<List<DocumentCategory>>>($"api/doccategory/{value}");
            if (!exists)
            {
                Snackbar!.Add("Selected document category does not exist" + message, Severity.Error);
                return;
            }

            extraFields = data?.Data[0].ExtraFields.Select((dd) =>
            {
                return new DocCategoryField
                {
                    FieldName = dd.FieldName,
                    FieldId = dd.FieldId,
                    Options = dd.Options,
                    CategoryId = dd.CategoryId
                };
            }).ToList() ?? new List<DocCategoryField>();

            (exists, var result, message) = await httpSvc!.GetAsync<ApiResponse<List<ListOption>>>($"api/listoptions/1?type=documentworkflow&category={value}");
            if (!exists)
                usersList = result?.Data ?? new List<ListOption>();
            //show dialog at this point



        }
        catch (Exception ex)
        {
            logger!.LogDebug(ex.Message);
        }
    }

    public bool ValidateForm() => success;


}
