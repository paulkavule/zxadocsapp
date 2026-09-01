using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Contracts;

// Create and edit a contract type and its fields on one form (ZD-115): with an Id the page
// loads and updates, without one it creates.
public partial class ContractTypeEditor
{
    [Parameter] public int Id { get; set; }

    [Inject] private IContractTypeClientService TypesApi { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private string name = string.Empty;
    private string description = string.Empty;
    private readonly List<FieldRow> fields = new();
    private bool loading = true;
    private bool saving;
    private bool canManage;

    private bool IsNew => Id == 0;

    // After first render, not OnInitializedAsync: the token is not hydrated there, so a deep
    // link would bounce someone who does hold the right.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        var perms = await Permissions.GetPermissions();
        canManage = perms.Contains(Permission.CreateContractTypes);

        if (!perms.HasAnyContractPermission())
        {
            Snackbar.Add("You do not have permission to view contract types.", Severity.Warning);
            Nav.NavigateTo("/dashboard");
            return;
        }
        if (IsNew && !canManage)
        {
            Snackbar.Add("You do not have permission to create contract types.", Severity.Warning);
            Nav.NavigateTo("/contract-types");
            return;
        }

        if (!IsNew) await LoadType();
        loading = false;
        StateHasChanged();
    }

    private async Task LoadType()
    {
        var (ok, type, error) = await TypesApi.Get(Id);
        if (!ok || type is null)
        {
            Snackbar.Add(error ?? "That contract type could not be found.", Severity.Warning);
            Nav.NavigateTo("/contract-types");
            return;
        }

        name = type.Name;
        description = type.Description;
        fields.Clear();
        fields.AddRange(type.Fields.OrderBy(f => f.Order).Select(FieldRow.From));
    }

    private void AddField() => fields.Add(new FieldRow());

    private void RemoveField(FieldRow row) => fields.Remove(row);

    private void Move(FieldRow row, int offset)
    {
        var from = fields.IndexOf(row);
        var to = from + offset;
        if (from < 0 || to < 0 || to >= fields.Count) return;
        fields.RemoveAt(from);
        fields.Insert(to, row);
    }

    private async Task Save()
    {
        // Checked here so the operator is told which row is wrong; the server re-checks it all.
        var problem = FirstProblem();
        if (problem is not null)
        {
            Snackbar.Add(problem, Severity.Warning);
            return;
        }

        saving = true;
        try
        {
            var request = new SaveContractTypeRequest
            {
                Name = name.Trim(),
                Description = description?.Trim() ?? string.Empty,
                Fields = fields.Select((row, index) => row.ToDto(index)).ToList(),
            };

            var (ok, _, error) = IsNew
                ? await TypesApi.Create(request)
                : await TypesApi.Update(Id, request);

            if (!ok)
            {
                Snackbar.Add(error ?? "Failed to save the contract type.", Severity.Error);
                return;
            }

            Snackbar.Add(IsNew ? "Contract type created." : "Contract type updated.", Severity.Success);
            Nav.NavigateTo("/contract-types");
        }
        finally
        {
            saving = false;
        }
    }

    private string? FirstProblem()
    {
        if (string.IsNullOrWhiteSpace(name)) return "A contract type needs a name.";

        var keys = fields.Select(f => (f.Key ?? string.Empty).Trim()).ToList();
        if (keys.Any(string.IsNullOrEmpty)) return "Every field needs a key.";
        if (keys.Count != keys.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            return "Field keys must be unique within a contract type.";

        var emptyDropdown = fields.FirstOrDefault(f =>
            f.Type == TemplateFieldType.Dropdown && f.SplitOptions().Count == 0);
        return emptyDropdown is null
            ? null
            : $"Dropdown field '{emptyDropdown.Key}' needs at least one option.";
    }

    // The editor's own row shape: options are typed as a comma-separated string, split on save.
    private sealed class FieldRow
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public TemplateFieldType Type { get; set; } = TemplateFieldType.Text;
        public bool Required { get; set; }
        public string OptionsCsv { get; set; } = string.Empty;
        public string DefaultValue { get; set; } = string.Empty;
        // Award and expiry (ZD-117): the label may be changed, nothing else.
        public bool IsSystem { get; set; }

        public static FieldRow From(ContractTypeFieldDto dto) => new()
        {
            Key = dto.Key,
            Label = dto.Label,
            Type = dto.FieldType,
            Required = dto.IsRequired,
            OptionsCsv = string.Join(", ", dto.Options),
            DefaultValue = dto.DefaultValue,
            IsSystem = dto.IsSystem,
        };

        public List<string> SplitOptions() => (OptionsCsv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        public ContractTypeFieldDto ToDto(int index) => new()
        {
            Key = (Key ?? string.Empty).Trim(),
            Label = string.IsNullOrWhiteSpace(Label) ? (Key ?? string.Empty).Trim() : Label.Trim(),
            FieldType = Type,
            IsRequired = Required,
            Options = Type == TemplateFieldType.Dropdown ? SplitOptions() : new List<string>(),
            DefaultValue = DefaultValue ?? string.Empty,
            IsSystem = IsSystem,
            // Position in the table is the field order.
            Order = index + 1,
        };
    }
}
