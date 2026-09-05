using System;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.State;
using zxadocsfe.Dtos;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocsui.State;

namespace zxadocsui.Components.DocWorkflow;

public partial class DocumentDetails
{
    [Inject] ISnackbar? Snackbar { get; set; } = default;
    [Inject] ILogger<DocumentDetails>? logger { set; get; }
    [Inject] private IHttpService httpSvc { get; set; } = default!;
    [Inject] private IUserSession session { get; set; } = default!;
    [Parameter] public Document? Document { set; get; }

    // MudStep destroys this component when its step is not active. The choices live on Document, but
    // the category fields do not, so the page hands them back on rebuild.
    [Parameter] public List<DocCategoryField>? SavedExtraFields { set; get; }

    // The list-options endpoint reads the organisation from the route and never falls back to the
    // token, so a hardcoded id here serves org 1's values to every tenant.
    private int orgId;
    // [Parameter] public bool? IsValid { set; get; }
    private List<ListOption> priorityList = new(), doctypeList = new(), docCatList = new(), usersList = new(), workflowList = new();
    private List<DocCategoryField> extraFields = new();
    string[] errors = { };
    public bool success;

    // Document.DueDate is a non-nullable DateTime, so "never set" is MinValue, not null, and
    // MudDatePicker works in DateTime? — hence the adapter.
    private DateTime? DueDate => Document is null || Document.DueDate == default
        ? null
        : Document.DueDate;

    private void DueDateChanged(DateTime? value)
    {
        if (Document is null || value is null) return;
        Document.DueDate = value.Value.Date;
    }

    // Not OnInitialized: the host swaps fields onto Document (the approved-draft handoff), and
    // the == default guard keeps this idempotent so a re-render never overwrites the author's pick.
    protected override void OnParametersSet()
    {
        if (Document is not null && Document.DueDate == default)
            Document.DueDate = DateTime.Today.AddDays(3);
    }

    protected override async Task OnInitializedAsync()
    {
        httpSvc.Initialize(AppConstants.HttpSchemes.Core);

        await Task.CompletedTask;
    }
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var userData = await session.GetCurrentUser();
            int.TryParse(userData.OrgId, out orgId);

            await loadPriorities();
            await loadDocumentTypes();
            await RestoreSelections();
            StateHasChanged();
        }
    }

    private async Task loadPriorities()
    {
        try
        {
            var (status, result, message) = await httpSvc!.GetAsync<ApiResponse<List<ListOption>>>($"api/listoptions/{orgId}?type=Priority");
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
            var (status, result, message) = await httpSvc!.GetAsync<ApiResponse<List<ListOption>>>($"api/listoptions/{orgId}?type=documenttype");
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
            docCatList.Clear();
            Document?.CategoryId = 0;
            Document?.TypeId = int.Parse(value);
            extraFields.Clear();
            await LoadCategories(int.Parse(value));
        }
        catch (Exception ex)
        {
            logger!.LogDebug(ex.Message);
        }
    }

    private async Task LoadCategories(int typeId)
    {
        var (status, result, message) = await httpSvc!.GetAsync<ApiResponse<List<ListOption>>>($"api/listoptions/{orgId}?type=documentcategory&category={typeId}");
        if (status == false || result?.Data == null)
        {
            Snackbar!.Add(message!, Severity.Error);
            return;
        }

        docCatList = result.Data;
    }

    /// <summary>Rebuilds what a destroyed component lost: the category list, and the category fields.</summary>
    private async Task RestoreSelections()
    {
        if (Document is null || Document.TypeId <= 0)
            return;

        await LoadCategories(Document.TypeId);

        // Copy: the picker clears this list when the type or category changes, and that must not
        // reach into the page's copy. The page re-harvests on every step change anyway.
        if (SavedExtraFields is { Count: > 0 })
            extraFields = SavedExtraFields.ToList();
    }
    private async Task DocCategoryChanged(string value)
    {
        try
        {
            extraFields.Clear();
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

            (exists, var result, message) = await httpSvc!.GetAsync<ApiResponse<List<ListOption>>>($"api/listoptions/{orgId}?type=documentworkflow&category={value}");
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

    public List<DocCategoryField> ExtraFields() => extraFields;


}
