using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;

namespace zxadocsui.Components.Pages.Dashboard.Templates;

// Create/Upload template + merge-field builder (/templates/new, FR-F2). Flow: create the
// template metadata -> upload version 1 (with progress) -> persist the merge fields ->
// navigate to the detail page. Requires the CreateTemplate permission (server also enforces).
public partial class TemplateCreate
{
    private const int MaxMb = 10;
    private const long MaxBytes = MaxMb * 1024L * 1024L;

    [Inject] private ITemplateClientService TemplatesApi { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private List<TemplateCategoryDto> categories = new();
    private string name = string.Empty;
    private int categoryId;
    private string description = string.Empty;

    private IBrowserFile? file;
    private string fileName = string.Empty;
    private string fileContentType = string.Empty;
    private string fileError = string.Empty;

    private readonly List<FieldRow> fields = new();
    private bool busy;
    private double uploadProgress;

    protected override async Task OnInitializedAsync()
    {
        if (!await Permissions.Has(Permission.CreateTemplate))
        {
            Snackbar.Add("You don't have permission to create templates.", Severity.Warning);
            Nav.NavigateTo("/templates");
            return;
        }
        var (ok, cats, _) = await TemplatesApi.GetCategories();
        if (ok) categories = cats.ToList();
    }

    private void OnFileSelected(IBrowserFile selected)
    {
        fileError = string.Empty;
        var ext = Path.GetExtension(selected.Name).ToLowerInvariant();
        if (ext is not (".pdf" or ".docx"))
        {
            fileError = "Only PDF or DOCX files are accepted.";
            file = null; fileName = string.Empty;
            return;
        }
        if (selected.Size > MaxBytes)
        {
            fileError = $"File exceeds the {MaxMb} MB limit.";
            file = null; fileName = string.Empty;
            return;
        }
        file = selected;
        fileName = selected.Name;
        fileContentType = ext == ".pdf"
            ? "application/pdf"
            : "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    }

    private void AddField() => fields.Add(new FieldRow { Order = fields.Count });
    private void RemoveField(FieldRow row) => fields.Remove(row);

    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(name)) { Snackbar.Add("Template name is required.", Severity.Warning); return; }
        if (categoryId <= 0) { Snackbar.Add("Please choose a category.", Severity.Warning); return; }
        if (file is null) { Snackbar.Add("Please choose a template file.", Severity.Warning); return; }

        var keys = fields.Select(f => (f.Key ?? "").Trim()).ToList();
        if (keys.Any(string.IsNullOrEmpty)) { Snackbar.Add("Every merge field needs a key.", Severity.Warning); return; }
        if (keys.Count != keys.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        { Snackbar.Add("Merge-field keys must be unique.", Severity.Warning); return; }

        busy = true;
        uploadProgress = 0;
        try
        {
            // 1. Create the template metadata.
            var (okCreate, template, createErr) = await TemplatesApi.Create(
                new CreateTemplateRequest { Name = name.Trim(), Description = description ?? "", CategoryId = categoryId });
            if (!okCreate || template is null) { Snackbar.Add(createErr ?? "Could not create the template.", Severity.Error); return; }

            // 2. Upload version 1 (buffer to memory so upload progress has a known length).
            using var ms = new MemoryStream();
            await using (var read = file.OpenReadStream(MaxBytes))
                await read.CopyToAsync(ms);
            ms.Position = 0;

            var progress = new Progress<double>(v => { uploadProgress = v; InvokeAsync(StateHasChanged); });
            var (okUpload, version, uploadErr) = await TemplatesApi.UploadVersion(template.Id, ms, fileName, progress);
            if (!okUpload || version is null)
            {
                Snackbar.Add(uploadErr ?? "Template created, but the file upload failed.", Severity.Error);
                Nav.NavigateTo($"/templates/{template.Id}");
                return;
            }

            // 3. Persist the merge fields against the new version.
            if (fields.Count > 0)
            {
                var req = new SetFieldsRequest { Fields = fields.Select(ToDto).ToList() };
                var (okFields, _, fieldsErr) = await TemplatesApi.SetFields(version.Id, req);
                if (!okFields) Snackbar.Add(fieldsErr ?? "Template uploaded, but saving fields failed.", Severity.Warning);
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
