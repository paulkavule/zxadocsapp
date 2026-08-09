using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Roles;

// Role list (/roles, ZD-93). Gated on the role-administration permissions. The server enforces
// each operation independently; this guard only keeps someone off a page whose buttons all 403.
public partial class RoleList
{
    [Inject] private IUserRoleClientService RolesApi { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private IDialogService Dialogs { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private List<UserRole> roles = new();
    private readonly Dictionary<int, int> permissionCounts = new();
    private bool loading = true;
    private bool canCreate;
    private bool canManage;

    // Runs after first render so the auth token is hydrated; in OnInitializedAsync the tokenless
    // permission fetch returns empty and the page would bounce a user who does hold the rights.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        var perms = await Permissions.GetPermissions();
        canCreate = perms.Contains(Permission.CreateRole);
        canManage = perms.Contains(Permission.ManageRole);

        if (!canCreate && !canManage)
        {
            Snackbar.Add("You do not have permission to manage roles.", Severity.Warning);
            Nav.NavigateTo("/dashboard");
            return;
        }

        await Load();
        StateHasChanged();
    }

    private async Task Load()
    {
        loading = true;
        permissionCounts.Clear();
        try
        {
            var (ok, data, error) = await RolesApi.List();
            if (!ok)
            {
                Snackbar.Add(error ?? "Failed to load roles.", Severity.Error);
                return;
            }
            roles = data.ToList();

            // One call per role — there is no bulk endpoint. Fine at org scale (tens of roles);
            // if that stops being true this wants a counts endpoint rather than a longer loop.
            foreach (var role in roles)
            {
                var (permsOk, values, _) = await Permissions.GetRolePermissions(role.RoleId);
                if (permsOk) permissionCounts[role.RoleId] = values.Length;
            }
        }
        finally
        {
            loading = false;
        }
    }

    private async Task ConfirmDelete(UserRole role)
    {
        // No endpoint reports how many users hold a role, so this warns rather than blocks.
        var confirmed = await Dialogs.ShowMessageBoxAsync(
            "Delete role",
            $"Delete \"{role.RoleName}\"? Anyone assigned this role loses the permissions it grants.",
            yesText: "Delete", cancelText: "Cancel");

        if (confirmed != true) return;

        var (ok, error) = await RolesApi.Delete(role.RoleId);
        if (!ok)
        {
            Snackbar.Add(error ?? "Failed to delete the role.", Severity.Error);
            return;
        }

        Snackbar.Add($"Deleted {role.RoleName}.", Severity.Success);
        // The caller may have just deleted a role they hold themselves.
        await Permissions.GetPermissions(forceRefresh: true);
        await Load();
    }
}
