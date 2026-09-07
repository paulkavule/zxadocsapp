using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Platform;

// The tenant list (ZD-131). Reachable by any system user; only a System Admin sees the write
// controls, and the API refuses the writes regardless of what this page shows.
public partial class OrganisationList
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private IDialogService Dialogs { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private List<Organisation> organisations = new();
    private bool loading = true;
    private bool canManage;

    // After first render, not OnInitializedAsync: the token is not hydrated there, so the fetch
    // returns empty and the page bounces someone who does hold the right.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        var roles = await Permissions.GetSystemRoles();
        if (roles.Count == 0)
        {
            Snackbar.Add("Organisation administration is available to system users only.", Severity.Warning);
            Nav.NavigateTo("/dashboard");
            return;
        }

        canManage = roles.Contains(SystemRole.SystemAdmin);
        await Load();
        StateHasChanged();
    }

    private async Task Load()
    {
        loading = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, result, error) = await Http
                .GetAsync<ApiPaginatedResponse<IEnumerable<Organisation>>>("api/organisations");

            if (!ok || result?.Data is null)
            {
                Snackbar.Add(error ?? "Failed to load organisations.", Severity.Error);
                return;
            }

            organisations = result.Data.OrderBy(org => org.Name).ToList();
        }
        finally
        {
            loading = false;
        }
    }

    private async Task ConfirmDelete(Organisation organisation)
    {
        var confirmed = await Dialogs.ShowMessageBoxAsync(
            "Delete organisation",
            $"Delete {organisation.Name}? This cannot be undone.",
            yesText: "Delete", cancelText: "Cancel");

        if (confirmed != true) return;

        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (ok, _, error) = await Http.ExecuteRequestAsync<ApiResponse<string>>(
            HttpVerb.Delete, $"api/organisations/{organisation.EntityId}");

        // The server refuses an organisation that still has users, roles, documents or departments
        // rather than orphaning them, and says how many — so the message is worth surfacing as is.
        Snackbar.Add(ok ? $"{organisation.Name} deleted." : ErrorMessage.Extract(error) ?? "Failed to delete.",
            ok ? Severity.Success : Severity.Error);

        if (ok) await Load();
        StateHasChanged();
    }
}
