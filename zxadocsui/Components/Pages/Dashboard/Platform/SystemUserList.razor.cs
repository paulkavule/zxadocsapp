using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Platform;

// The platform's own accounts (ZD-131). Read only - see the note in the markup.
public partial class SystemUserList
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private ActingOrganisationState Acting { get; set; } = default!;

    private List<User> users = new();
    private bool loading = true;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        if ((await Permissions.GetSystemRoles()).Count == 0)
        {
            Snackbar.Add("This page is available to system users only.", Severity.Warning);
            Nav.NavigateTo("/dashboard");
            return;
        }

        await Load();
        StateHasChanged();
    }

    private async Task Load()
    {
        loading = true;
        try
        {
            // Without the acting-organisation header: these are the system organisation's own
            // users, not whichever tenant the header currently names.
            Http.Initialize(AppConstants.HttpSchemes.Core);
            using var scope = Acting.Suppressed();
            var (ok, result, error) = await Http.GetAsync<ApiResponse<List<User>>>("api/users");

            if (!ok || result?.Data is null)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to load system users.", Severity.Error);
                return;
            }

            users = result.Data;
        }
        finally
        {
            loading = false;
        }
    }

    private static string RoleNames(User user) =>
        user.Roles is { Length: > 0 }
            ? string.Join(", ", user.Roles.Select(role => role.RoleName))
            : "-";
}
