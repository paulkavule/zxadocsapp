using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.Components.Custom;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Templates;

// Create template + merge-field builder + rich-text body (/templates/new, FR-F2). The
// contract is authored in a rich text editor as HTML with {{key}} tokens. Flow: create the
// template -> upload the HTML body as version 1 -> persist the merge fields -> navigate.
// Requires the CreateTemplate permission (server also enforces).
public partial class TemplateCreate
{
    [Inject] private ITemplateClientService TemplatesApi { get; set; } = default!;
    [Inject] private IContractTypeClientService ContractTypesApi { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private RichTextEditor? editorRef;
    private List<TemplateCategoryDto> categories = new();
    private string name = string.Empty;
    private int categoryId;
    private string description = string.Empty;

    private readonly List<FieldRow> fields = new();
    private bool busy;
    private double uploadProgress;

    // A contract template (ZD-116): 0 means none, so the author builds the fields instead.
    private List<ContractTypeDto> contractTypes = new();
    private int contractTypeId;
    private List<ContractTypeFieldDto> typeFields = new();

    private bool UsesContractType => IsContractCategory && contractTypeId > 0;

    // Contains, not equals: this org's category is "Contract", the test fixtures use "Contracts",
    // and an org may well name it "Service Contracts".
    private bool IsContractCategory =>
        (categories.FirstOrDefault(c => c.Id == categoryId)?.Name ?? string.Empty)
            .Contains("contract", StringComparison.OrdinalIgnoreCase);

    private string SelectedTypeName =>
        contractTypes.FirstOrDefault(t => t.Id == contractTypeId)?.Name ?? string.Empty;

    // "Insert field" offers whichever set is live, so a contract body can only reference keys
    // the type actually defines.
    private IEnumerable<string> TokenKeys => UsesContractType
        ? typeFields.Select(f => f.Key)
        : fields.Select(f => f.Key).Where(k => !string.IsNullOrWhiteSpace(k));

    // Guard + authed loads run in OnAfterRenderAsync so the token is hydrated first; in
    // OnInitializedAsync the tokenless permission call would 401 and wrongly bounce an authorized
    // creator on a cold load / refresh.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        await Session.GetCurrentUser();   // hydrate the auth token before any authed call
        if (!await Permissions.Has(Permission.CreateTemplate))
        {
            Snackbar.Add("You don't have permission to create templates.", Severity.Warning);
            Nav.NavigateTo("/templates");
            return;
        }
        var (ok, cats, _) = await TemplatesApi.GetCategories();
        if (ok) categories = cats.ToList();

        var (typesOk, types, _) = await ContractTypesApi.List();
        if (typesOk) contractTypes = types.ToList();
        // Categories are configured per organisation (maintained under Organisation settings).
        if (categoryId == 0 && categories.Count > 0) categoryId = categories[0].Id;
        StateHasChanged();
    }

    // Moving off a contract category drops the selection with the dropdown, so a type can never
    // be submitted from a field the author can no longer see.
    private void OnCategoryChanged()
    {
        if (IsContractCategory) return;
        contractTypeId = 0;
        typeFields.Clear();
    }

    // Both selects bind an int id, so without these they render the raw number.
    private string CategoryName(int id) =>
        categories.FirstOrDefault(c => c.Id == id)?.Name ?? string.Empty;

    private string ContractTypeLabel(int id) => id == 0
        ? "None — define fields below"
        : contractTypes.FirstOrDefault(t => t.Id == id)?.Name ?? string.Empty;

    private void OnContractTypeChanged() =>
        typeFields = contractTypes.FirstOrDefault(t => t.Id == contractTypeId)?.Fields
            .OrderBy(f => f.Order).ToList() ?? new List<ContractTypeFieldDto>();

    private void AddField() => fields.Add(new FieldRow { Order = fields.Count });
    private void RemoveField(FieldRow row) => fields.Remove(row);

    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(name)) { Snackbar.Add("Template name is required.", Severity.Warning); return; }
        if (categoryId <= 0) { Snackbar.Add("Please choose a category.", Severity.Warning); return; }

        if (!UsesContractType)
        {
            var keys = fields.Select(f => (f.Key ?? "").Trim()).ToList();
            if (keys.Any(string.IsNullOrEmpty)) { Snackbar.Add("Every merge field needs a key.", Severity.Warning); return; }
            if (keys.Count != keys.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            { Snackbar.Add("Merge-field keys must be unique.", Severity.Warning); return; }
        }

        var bodyHtml = editorRef is null ? string.Empty : await editorRef.GetHtmlAsync();
        var bodyDelta = editorRef is null ? string.Empty : await editorRef.GetDeltaAsync();
        if (TemplateHtml.IsBodyEmpty(bodyHtml))
        { Snackbar.Add("The contract body is empty.", Severity.Warning); return; }

        busy = true;
        uploadProgress = 0;
        try
        {
            // 1. Create the template metadata.
            var (okCreate, template, createErr) = await TemplatesApi.Create(
                new CreateTemplateRequest
                {
                    Name = name.Trim(),
                    Description = description ?? "",
                    CategoryId = categoryId,
                    ContractTypeId = UsesContractType ? contractTypeId : null,
                });
            if (!okCreate || template is null) { Snackbar.Add(createErr ?? "Could not create the template.", Severity.Error); return; }

            // 2. Upload the authored HTML as version 1.
            var bytes = System.Text.Encoding.UTF8.GetBytes(DocumentHtml.Wrap(name.Trim(), bodyHtml, bodyDelta));
            using var ms = new MemoryStream(bytes);
            var progress = new Progress<double>(v => { uploadProgress = v; InvokeAsync(StateHasChanged); });
            var (okUpload, version, uploadErr) = await TemplatesApi.UploadVersion(template.Id, ms, "template.html", progress);
            if (!okUpload || version is null)
            {
                Snackbar.Add(uploadErr ?? "Template created, but saving the body failed.", Severity.Error);
                Nav.NavigateTo($"/templates/{template.Id}");
                return;
            }

            // 3. Persist the merge fields against the new version. A contract template has none of
            // its own — its fields live on the contract type, and the server rejects them here.
            if (!UsesContractType && fields.Count > 0)
            {
                var req = new SetFieldsRequest { Fields = fields.Select(ToDto).ToList() };
                var (okFields, _, fieldsErr) = await TemplatesApi.SetFields(version.Id, req);
                if (!okFields) Snackbar.Add(fieldsErr ?? "Body saved, but saving fields failed.", Severity.Warning);
            }

            Snackbar.Add("Template created.", Severity.Success);
            Nav.NavigateTo($"/templates/{template.Id}");
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Something went wrong: {ex.Message}", Severity.Error);
        }
        finally
        {
            busy = false;
        }
    }

    private TemplateFieldDto ToDto(FieldRow f) => new()
    {
        Key = f.Key.Trim(),
        Label = string.IsNullOrWhiteSpace(f.Label) ? f.Key.Trim() : f.Label.Trim(),
        FieldType = f.Type,
        IsRequired = f.Required,
        Options = f.Type == TemplateFieldType.Dropdown
            ? (f.OptionsCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            : new List<string>(),
        DefaultValue = string.Empty,
        Order = f.Order,
    };

    // Editable row model for the merge-field builder.
    private sealed class FieldRow
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public TemplateFieldType Type { get; set; } = TemplateFieldType.Text;
        public bool Required { get; set; }
        public string OptionsCsv { get; set; } = string.Empty;
        public int Order { get; set; }
    }
}
