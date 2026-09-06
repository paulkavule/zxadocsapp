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
    [Inject] IPermissionClientService permissions { get; set; } = default!;
    private List<ListOption> doctypeList = new(), docCatList = new();
    List<WorkFlow> workflowList = new();
    readonly List<FieldRow> categoryFields = new();
    string docCategory = "", docTypeName = "", docCategoryName = "";
    bool rearranged = false, updating, saving;
    bool canManage, savingFields;
    int orgId = 0;

    /// <summary>True once a category is chosen, which is what tells an empty list apart from no selection.</summary>
    bool CategorySelected => !string.IsNullOrEmpty(docCategory) && docCategory != "0";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            if (firstRender)
            {
                var userData = await session!.GetCurrentUser();
                httpSvc.Initialize(AppConstants.HttpSchemes.Core);
                int.TryParse(userData.OrgId, out orgId);
                canManage = (await permissions.GetPermissions()).Contains(Permission.ManageRole);
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
            var (status, result, message) = await httpSvc!.GetAsync<ApiResponse<List<ListOption>>>($"api/listoptions/{orgId}?type=documenttype");
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
            // Changing the type invalidates the category and everything shown for it.
            docCatList.Clear();
            docCategory = "";
            docCategoryName = "";
            workflowList.Clear();
            categoryFields.Clear();
            rearranged = false;
            docTypeName = NameOf(doctypeList, value);

            if (value == "0")
                return;

            var (status, result, message) = await httpSvc!.GetAsync<ApiResponse<List<ListOption>>>($"api/listoptions/{orgId}?type=documentcategory&category={value}");
            if (status == false || result?.Data == null)
            {
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
        docCategory = value == "0" ? "" : value;
        docCategoryName = NameOf(docCatList, value);
        rearranged = false;

        if (!CategorySelected)
        {
            workflowList.Clear();
            categoryFields.Clear();
            return;
        }

        await LoadCategoryWorkflow();
        await LoadCategoryFields();
    }

    /// <summary>Fetches the selected category's levels. A failed fetch leaves the list empty rather than stale.</summary>
    async Task LoadCategoryWorkflow()
    {
        workflowList.Clear();

        var (status, result, message) = await httpSvc!.GetAsync<ApiResponse<List<WorkFlow>>>($"api/doccategoryworkflow/category/{docCategory}");
        if (status == false || result?.Data == null)
        {
            Snackbar!.Add(message!, Severity.Error);
            return;
        }

        workflowList = result.Data;
    }

    static string NameOf(List<ListOption> options, string id) =>
        options.FirstOrDefault(o => o.Id.ToString() == id && o.Id != 0)?.Name ?? "";

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
            rearranged = false;
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

    /// <summary>Defines a category's whole chain in one pass, for a category that has none yet.</summary>
    async Task CreateDefaultWorkflow()
    {
        var levels = await sideDialog.Show<DefaultWorkflowBuilder, List<WorkFlow>>(title: "Create default workflow");
        if (levels == null || levels.Count == 0)
            return;

        Snackbar.Clear();
        try
        {
            saving = true;
            var (proceed, _, message) = await httpSvc.ExecuteRequestAsync<ApiResponse<object>>(
                HttpVerb.Post, $"api/doccategoryworkflow/category/{docCategory}/default", levels);

            if (proceed == false)
            {
                Snackbar.Add(string.IsNullOrEmpty(message) ? "Couldn't create the workflow" : message, Severity.Error);
                return;
            }

            await LoadCategoryWorkflow();
            Snackbar.Add($"Workflow created with {workflowList.Count} levels", Severity.Success);
        }
        catch (Exception ee)
        {
            Snackbar.Add(ee.Message, Severity.Error);
            logger.LogError(ee, ee.Message);
        }
        finally
        {
            saving = false;
        }
    }

    async Task AddStepToCategory()
    {
        var result = await sideDialog.Show<EditDocWorkflow, WorkFlow>(title: "Choose role");
        Snackbar.Clear();
        if (result == null)
            return;

        var data = new zxadocslib.Dtos.WorkFlow
        {
            IsRequired = result.IsRequired,
            RoleId = result.RoleId,
            Level = workflowList.Count + 1,
            OrganisationId = orgId,
            CategoryId = int.Parse(docCategory),
        };
        var (proceed, _, message) = await httpSvc.ExecuteRequestAsync<ApiResponse<object>>(HttpVerb.Post, "api/doccategoryworkflow", data);

        Snackbar!.Clear();
        Snackbar!.Add(proceed ? "Success" : message ?? "Something went wrong", proceed ? Severity.Success : Severity.Error);

        // Reload rather than append: the new step's server id is what a later reorder saves against.
        if (proceed)
            await LoadCategoryWorkflow();
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
        };
        var (proceed, _, message) = await httpSvc.ExecuteRequestAsync<ApiResponse<object>>(HttpVerb.Post, "api/doccategoryworkflow", data);

        Snackbar!.Clear();
        Snackbar!.Add(proceed ? "Success" : message ?? "Something went wrong", proceed ? Severity.Success : Severity.Error);

    }

    // ---------------------------------------------------------- category fields (ZD-126)

    // Party is a contract-drafting concept and has no meaning on a document category.
    static readonly TemplateFieldType[] FieldTypes =
    [
        TemplateFieldType.Text, TemplateFieldType.MultilineText, TemplateFieldType.Number,
        TemplateFieldType.Currency, TemplateFieldType.Date, TemplateFieldType.Boolean,
        TemplateFieldType.Dropdown
    ];

    /// <summary>Fetches the category's extra fields. A failed fetch leaves the list empty rather than stale.</summary>
    async Task LoadCategoryFields()
    {
        categoryFields.Clear();

        var (status, result, message) = await httpSvc!.GetAsync<ApiResponse<List<CategoryField>>>(
            $"api/doccategory/{docCategory}/fields");
        if (status == false || result?.Data == null)
            return;

        categoryFields.AddRange(result.Data.Select(FieldRow.From));
    }

    void AddField() => categoryFields.Add(new FieldRow());

    void RemoveField(FieldRow row) => categoryFields.Remove(row);

    void MoveField(FieldRow row, int offset)
    {
        var from = categoryFields.IndexOf(row);
        var to = from + offset;
        if (from < 0 || to < 0 || to >= categoryFields.Count)
            return;

        categoryFields.RemoveAt(from);
        categoryFields.Insert(to, row);
    }

    async Task SaveFields()
    {
        // Checked here so the operator is told which row is wrong; the server re-checks it all.
        var problem = FirstFieldProblem();
        if (problem is not null)
        {
            Snackbar.Add(problem, Severity.Warning);
            return;
        }

        savingFields = true;
        try
        {
            var payload = categoryFields.Select((row, index) => row.ToDto(int.Parse(docCategory), index)).ToList();
            var (proceed, _, message) = await httpSvc.ExecuteRequestAsync<ApiResponse<int>>(
                HttpVerb.Put, $"api/doccategory/{docCategory}/fields", payload);

            Snackbar.Clear();
            Snackbar.Add(proceed ? "Category fields saved" : message ?? "Something went wrong",
                proceed ? Severity.Success : Severity.Error);

            // Reload rather than trust the local rows: a new field is only issued its FieldId here.
            if (proceed)
                await LoadCategoryFields();
        }
        finally
        {
            savingFields = false;
        }
    }

    string? FirstFieldProblem()
    {
        var names = categoryFields.Select(f => (f.Name ?? "").Trim()).ToList();
        if (names.Any(string.IsNullOrEmpty))
            return "Every field needs a name.";

        if (names.Count != names.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            return "Field names must be unique within a category.";

        var emptyDropdown = categoryFields.FirstOrDefault(f =>
            f.Type == TemplateFieldType.Dropdown && f.SplitOptions().Count == 0);

        return emptyDropdown is null ? null : $"Dropdown field '{emptyDropdown.Name}' needs at least one option.";
    }

    /// <summary>The editor's own row: options are typed as a comma-separated string, split on save.</summary>
    sealed class FieldRow
    {
        // 0 until the server issues one. Carried back on save so a rename keeps the field identity
        // that captured values point at.
        public int FieldId { get; set; }
        public string Name { get; set; } = "";
        public TemplateFieldType Type { get; set; } = TemplateFieldType.Text;
        public bool Required { get; set; }
        public string OptionsCsv { get; set; } = "";

        public static FieldRow From(CategoryField dto) => new()
        {
            FieldId = dto.FieldId,
            Name = dto.FieldName,
            Type = dto.FieldType,
            Required = dto.IsRequired,
            OptionsCsv = string.Join(", ", dto.Options.Select(o => o.Name)),
        };

        public List<string> SplitOptions() => (OptionsCsv ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        public CategoryField ToDto(int categoryId, int index) => new()
        {
            CategoryId = categoryId,
            FieldId = FieldId,
            FieldName = (Name ?? "").Trim(),
            FieldType = Type,
            IsRequired = Required,
            // Position in the table is the field order.
            Order = index + 1,
            IsActive = true,
            Options = Type == TemplateFieldType.Dropdown
                ? SplitOptions().Select(o => new ListOption { Name = o }).ToList()
                : new List<ListOption>(),
        };
    }
}
