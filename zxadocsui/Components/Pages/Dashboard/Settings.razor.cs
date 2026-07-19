using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard;

// Organisation Settings — generic list-values manager (add/edit/delete the org's configurable
// lookups). Template categories are the "TemplateCategory" list; more list types can be added
// to `lists`. Mutations require the Admin role (server also enforces).
public partial class Settings
{
    [Inject] private IListOptionClientService ListOptions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    // The configurable lists exposed here. Add a row to surface another lookup.
    private readonly List<(string Label, string Type)> lists = new()
    {
        ("Template categories", "TemplateCategory"),
    };

    private string selectedType = "TemplateCategory";
    private readonly List<ListOption> values = new();
    private int editingId;
    private string editName = string.Empty;
    private bool busy;

    protected override async Task OnInitializedAsync()
    {
        var user = await Session.GetCurrentUser();
        if (!string.Equals(user.RoleName, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            Snackbar.Add("Organisation settings are available to admins only.", Severity.Warning);
            Nav.NavigateTo("/dashboard");
            return;
        }
        await LoadValues();
    }

    private async Task OnListChanged(string type)
    {
        selectedType = type;
        CancelEdit();
        await LoadValues();
    }

    private async Task LoadValues()
    {
        values.Clear();
        var (ok, data, error) = await ListOptions.Get(selectedType);
        if (!ok) { Snackbar.Add(error ?? "Failed to load values.", Severity.Error); return; }
        values.AddRange(data);
    }

    private void Edit(ListOption v) { editingId = v.Id; editName = v.Name; }
    private void CancelEdit() { editingId = 0; editName = string.Empty; }

    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(editName)) { Snackbar.Add("A value is required.", Severity.Warning); return; }
        busy = true;
        try
        {
            var (ok, error) = editingId == 0
                ? await ListOptions.Create(editName.Trim(), selectedType)
                : await ListOptions.Update(editingId, editName.Trim(), selectedType);
            if (!ok) { Snackbar.Add(error ?? "Save failed.", Severity.Error); return; }
            Snackbar.Add(editingId == 0 ? "Value added." : "Value updated.", Severity.Success);
            CancelEdit();
            await LoadValues();
        }
        finally { busy = false; }
    }

    private async Task Delete(ListOption v)
    {
        busy = true;
        try
        {
            var (ok, error) = await ListOptions.Delete(v.Id);
            if (!ok) { Snackbar.Add(error ?? "Delete failed.", Severity.Error); return; }
            Snackbar.Add("Value deleted.", Severity.Success);
            if (editingId == v.Id) CancelEdit();
            await LoadValues();
        }
        finally { busy = false; }
    }
}
