using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Platform;

// Departments for whichever organisation is selected (ZD-131). Inline add/edit rather than a
// separate editor page: a department is a name, and a second route for one field is not worth it.
public partial class DepartmentList : IDisposable
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private IDialogService Dialogs { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private ActingOrganisationState Acting { get; set; } = default!;

    private List<ListOption> departments = new();
    private string editName = string.Empty;
    private int editingId;
    private bool loading = true;
    private bool busy;
    private bool canManage;

    protected override void OnInitialized() => Acting.Changed += OnActingChanged;

    public void Dispose() => Acting.Changed -= OnActingChanged;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        var roles = await Permissions.GetSystemRoles();
        if (roles.Count == 0)
        {
            Snackbar.Add("Department administration is available to system users only.", Severity.Warning);
            Nav.NavigateTo("/dashboard");
            return;
        }

        canManage = roles.Contains(SystemRole.SystemAdmin);
        await Load();
        StateHasChanged();
    }

    /// <summary>Switching organisation in the header reloads the list without a re-login.</summary>
    private async Task OnActingChanged()
    {
        CancelEdit();
        await Load();
        await InvokeAsync(StateHasChanged);
    }

    private async Task Load()
    {
        loading = true;
        try
        {
            departments.Clear();
            if (Acting.IsAllOrganisations)
                return;

            // The acting organisation rides on a header applied by HttpService, so the URL is the
            // same whichever tenant is selected.
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, result, error) = await Http.GetAsync<ApiResponse<List<ListOption>>>("api/departments");

            if (!ok || result?.Data is null)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to load departments.", Severity.Error);
                return;
            }

            departments = result.Data;
        }
        finally
        {
            loading = false;
        }
    }

    private void BeginEdit(ListOption department)
    {
        editingId = department.Id;
        editName = department.Name;
    }

    private void CancelEdit()
    {
        editingId = 0;
        editName = string.Empty;
    }

    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(editName))
        {
            Snackbar.Add("A department needs a name.", Severity.Warning);
            return;
        }

        busy = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var body = new { Name = editName.Trim(), Description = string.Empty };
            var (ok, _, error) = editingId == 0
                ? await Http.ExecuteRequestAsync<ApiResponse<int>>(HttpVerb.Post, "api/departments", body)
                : await Http.ExecuteRequestAsync<ApiResponse<int>>(HttpVerb.Put, $"api/departments/{editingId}", body);

            if (!ok)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to save the department.", Severity.Error);
                return;
            }

            Snackbar.Add(editingId == 0 ? "Department added." : "Department updated.", Severity.Success);
            CancelEdit();
            await Load();
        }
        finally
        {
            busy = false;
        }
    }

    private async Task ConfirmDelete(ListOption department)
    {
        var confirmed = await Dialogs.ShowMessageBoxAsync(
            "Delete department", $"Delete {department.Name}?", yesText: "Delete", cancelText: "Cancel");

        if (confirmed != true) return;

        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (ok, _, error) = await Http.ExecuteRequestAsync<ApiResponse<int>>(
            HttpVerb.Delete, $"api/departments/{department.Id}");

        // The server refuses a department that still has users in it and says how many.
        Snackbar.Add(ok ? "Department deleted." : ErrorMessage.Extract(error) ?? "Failed to delete.",
            ok ? Severity.Success : Severity.Error);

        if (ok) await Load();
        StateHasChanged();
    }
}
