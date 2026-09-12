using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.Srevices;
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
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    private List<ListOption> departments = new();
    private List<ListOption> organisations = new();
    private bool systemUser;
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
        systemUser = roles.Count > 0;

        // Two ways in: the platform's System Admin acting on a chosen customer, or the
        // organisation's own administrator holding ManageDepartments, confined to its own.
        var ownAdministrator = await Permissions.Has(Permission.ManageDepartments);
        if (!systemUser && !ownAdministrator)
        {
            Snackbar.Add("Department administration requires the department permission.", Severity.Warning);
            Nav.NavigateTo("/dashboard");
            return;
        }

        canManage = roles.Contains(SystemRole.SystemAdmin) || ownAdministrator;
        if (systemUser) await LoadOrganisations();
        await Load();
        StateHasChanged();
    }

    /// <summary>The organisations a system user may act on. Read is open to any system user.</summary>
    private async Task LoadOrganisations()
    {
        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (ok, result, _) = await Http.GetAsync<ApiResponse<List<ListOption>>>("api/organisations/options");
        if (ok && result?.Data is not null)
            organisations = result.Data;
    }

    /// <summary>
    /// For a system user, no selection means their own organisation -- the reserved system one --
    /// so both that and "all" have to name a customer first. An organisation's own administrator
    /// has nothing to choose: the token already names the only organisation they can touch.
    /// </summary>
    private bool NeedsOrganisation =>
        systemUser && (Acting.IsAllOrganisations || Acting.OrganisationId is not > 0);

    /// <summary>Switching organisation in the header reloads the list without a re-login.</summary>
    private async Task OnActingChanged()
    {
        await Load();
        await InvokeAsync(StateHasChanged);
    }

    private async Task Load()
    {
        loading = true;
        try
        {
            departments.Clear();
            if (NeedsOrganisation)
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

    private Task OpenNew() => OpenEditor(0, string.Empty);

    private Task OpenEdit(ListOption department) => OpenEditor(department.Id, department.Name);

    /// <summary>The editor lives in the side panel; it reports back whether it actually saved.</summary>
    private async Task OpenEditor(int departmentId, string departmentName)
    {
        var saved = await SideDialog.Show<DepartmentEditor, bool?>(
            new Dictionary<string, object>
            {
                { nameof(DepartmentEditor.DepartmentId), departmentId },
                { nameof(DepartmentEditor.DepartmentName), departmentName },
                { nameof(DepartmentEditor.Organisations), organisations },
                { nameof(DepartmentEditor.SystemUser), systemUser },
            },
            title: departmentId == 0 ? "New department" : "Edit department");

        if (saved == true)
            await Load();
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
