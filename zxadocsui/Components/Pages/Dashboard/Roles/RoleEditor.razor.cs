using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Components;
using MudBlazor;
// FluentValidation also defines Severity; the snackbar calls want MudBlazor's.
using Severity = MudBlazor.Severity;
using zxadocsfe.Services;
using zxadocslib.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Roles;

// Create/edit a role and its permission set on one form (ZD-93). Admin-gated like RoleList.
public partial class RoleEditor
{
    [Parameter] public int Id { get; set; }

    [Inject] private IUserRoleClientService RolesApi { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private MudForm form = default!;
    private readonly RoleFormModel model = new();
    private readonly RoleFormValidator validator = new();
    private bool isValid;
    private bool saving;
    private bool loading = true;
    // CreateRole lets you name a role; only ManageRole may set what it grants.
    private bool canSetPermissions;

    private readonly HashSet<int> selected = new();
    // Whatever implicit-default rows the role already had. Preserved verbatim on save so the
    // editor never silently adds or deletes a grant the user could not see or change.
    private readonly HashSet<int> implicitGranted = new();
    private Dictionary<string, List<PermissionItem>> groups = new();

    private bool IsNew => Id == 0;

    private static readonly HashSet<int> ImplicitValues =
        PermissionSet.Defaults.Select(p => (int)p).ToHashSet();

    protected override async Task OnInitializedAsync()
    {
        await Session.GetCurrentUser();

        // Creating and editing are separate rights: CreateRole alone may open /roles/new but not
        // an existing role. Setting the permission set itself always needs ManageRole, which the
        // server enforces on PUT /api/roles/{id}/permissions.
        var perms = await Permissions.GetPermissions();
        var allowed = IsNew ? perms.Contains(Permission.CreateRole) : perms.Contains(Permission.ManageRole);
        if (!allowed)
        {
            Snackbar.Add("You do not have permission to manage roles.", Severity.Warning);
            Nav.NavigateTo("/roles");
            return;
        }
        canSetPermissions = perms.Contains(Permission.ManageRole);

        var (ok, catalogue, error) = await Permissions.GetCatalogue();
        if (!ok)
        {
            Snackbar.Add(error ?? "Failed to load the permission catalogue.", Severity.Error);
            loading = false;
            return;
        }
        groups = Group(catalogue);

        if (!IsNew) await LoadRole();
        loading = false;
    }

    private async Task LoadRole()
    {
        var (ok, role, error) = await RolesApi.Get(Id);
        if (!ok || role is null)
        {
            Snackbar.Add(error ?? "Role not found.", Severity.Error);
            Nav.NavigateTo("/roles");
            return;
        }
        model.RoleName = role.RoleName;

        var (permsOk, values, permsError) = await Permissions.GetRolePermissions(Id);
        if (!permsOk)
        {
            Snackbar.Add(permsError ?? "Failed to load the role's permissions.", Severity.Error);
            return;
        }
        foreach (var v in values)
        {
            if (ImplicitValues.Contains(v)) implicitGranted.Add(v);
            else selected.Add(v);
        }
    }

    private bool IsChecked(int value) => IsImplicit(value) || selected.Contains(value);

    private static bool IsImplicit(int value) => ImplicitValues.Contains(value);

    private void Toggle(int value, bool on)
    {
        if (IsImplicit(value)) return;   // disabled in the UI; guard the handler too
        if (on) selected.Add(value); else selected.Remove(value);
    }

    private async Task Save()
    {
        await form.ValidateAsync();
        if (!isValid) return;

        saving = true;
        try
        {
            var roleId = Id;

            if (IsNew)
            {
                var (created, newId, createError) = await RolesApi.Create(model.RoleName.Trim());
                if (!created || newId == 0)
                {
                    Snackbar.Add(createError ?? "Failed to create the role.", Severity.Error);
                    return;
                }
                roleId = newId;
            }
            else
            {
                var (updated, updateError) = await RolesApi.Update(Id, model.RoleName.Trim());
                if (!updated)
                {
                    Snackbar.Add(updateError ?? "Failed to rename the role.", Severity.Error);
                    return;
                }
            }

            // Without ManageRole the server rejects the permission write, so don't attempt it —
            // the role is saved by name and someone holding ManageRole grants it later.
            if (canSetPermissions)
            {
                // Second call, no shared transaction with the first. If it fails after a CREATE
                // the role exists granting nothing, so adopt its id rather than navigating away —
                // otherwise a retry would create a duplicate and the failure would look like a
                // no-op.
                var payload = selected.Concat(implicitGranted);
                var (permsSaved, permsError) = await Permissions.SetRolePermissions(roleId, payload);
                if (!permsSaved)
                {
                    Id = roleId;
                    Snackbar.Add(
                        $"Role saved, but its permissions were not: {permsError ?? "unknown error"}. It currently grants nothing — retry Save.",
                        Severity.Error);
                    return;
                }
            }

            // The caller may have just edited a role they hold; the cached permission set drives
            // the sidebar and every page gate, so refresh it rather than wait for a re-login.
            await Permissions.GetPermissions(forceRefresh: true);

            Snackbar.Add(IsNew ? "Role created." : "Role updated.", Severity.Success);
            Nav.NavigateTo("/roles");
        }
        finally
        {
            saving = false;
        }
    }

    // The enum's decades already encode the module, so grouping needs no server metadata:
    // 1x Legal Templates, 2x Contract Drafting, 3x Audit and Reporting, 7x Workflows. An
    // unrecognised decade still renders.
    private static Dictionary<string, List<PermissionItem>> Group(IEnumerable<PermissionItem> all) =>
        all.GroupBy(p => p.Value / 10)
           .OrderBy(g => g.Key)
           .ToDictionary(g => GroupName(g.Key), g => g.OrderBy(p => p.Value).ToList());

    private static string GroupName(int decade) => decade switch
    {
        0 => "Defaults",
        1 => "Legal Templates",
        2 => "Contract Drafting",
        3 => "Audit and Reporting",
        4 => "User Management",
        5 => "Role Management",
        6 => "Contract Management",
        7 => "Workflow Management",
        _ => "Other",
    };

    // Labels are derived from the enum member name rather than a hand-kept map, so a new
    // Permission shows up correctly with no UI change: "ApproveTemplate" -> "Approve template".
    private static string Label(string name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])) sb.Append(' ').Append(char.ToLowerInvariant(name[i]));
            else sb.Append(name[i]);
        }
        return sb.ToString();
    }
}

public class RoleFormModel
{
    public string RoleName { get; set; } = string.Empty;
}

public class RoleFormValidator : AbstractValidator<RoleFormModel>
{
    public RoleFormValidator()
    {
        RuleFor(x => x.RoleName).NotEmpty().WithMessage("Role name is required")
            .MinimumLength(2).WithMessage("Role name is too short")
            .MaximumLength(40).WithMessage("Role name is too long");
    }

    public Func<object, string, Task<IEnumerable<string>>> ValidateValue => async (model, propertyName) =>
    {
        var result = await ValidateAsync(
            ValidationContext<RoleFormModel>.CreateWithOptions((RoleFormModel)model, x => x.IncludeProperties(propertyName)));
        return result.IsValid ? Array.Empty<string>() : result.Errors.Select(e => e.ErrorMessage);
    };
}
