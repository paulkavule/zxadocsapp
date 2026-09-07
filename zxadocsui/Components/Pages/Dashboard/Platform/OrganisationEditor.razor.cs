using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Platform;

// Create and edit a tenant on one form (ZD-131). Keyed on EntityId rather than an int id, because
// that is the handle the organisation endpoints take.
public partial class OrganisationEditor
{
    [Parameter] public string? EntityId { get; set; }

    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private Organisation organisation = new();
    private bool loading = true;
    private bool saving;

    private bool IsNew => string.IsNullOrWhiteSpace(EntityId);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        var roles = await Permissions.GetSystemRoles();

        // Both tiers, so a deep link cannot slip past: a non-system user goes to the dashboard, a
        // system user who is not an admin goes back to the list they can legitimately read.
        if (roles.Count == 0)
        {
            Snackbar.Add("Organisation administration is available to system users only.", Severity.Warning);
            Nav.NavigateTo("/dashboard");
            return;
        }

        if (!roles.Contains(SystemRole.SystemAdmin))
        {
            Snackbar.Add("Changing organisations requires the System Admin role.", Severity.Warning);
            Nav.NavigateTo("/settings/organisations");
            return;
        }

        if (!IsNew) await LoadOrganisation();
        loading = false;
        StateHasChanged();
    }

    private async Task LoadOrganisation()
    {
        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (ok, result, error) = await Http
            .GetAsync<ApiResponse<Organisation>>($"api/organisations/{EntityId}");

        if (!ok || result?.Data is null)
        {
            Snackbar.Add(ErrorMessage.Extract(error) ?? "That organisation could not be found.", Severity.Warning);
            Nav.NavigateTo("/settings/organisations");
            return;
        }

        organisation = result.Data;
    }

    private async Task Save()
    {
        // Checked here so the operator is told what is wrong; the server re-checks it all.
        if (string.IsNullOrWhiteSpace(organisation.Name))
        {
            Snackbar.Add("An organisation needs a name.", Severity.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(organisation.Email) && string.IsNullOrWhiteSpace(organisation.PhoneNumber))
        {
            Snackbar.Add("Give the organisation an email or a phone number.", Severity.Warning);
            return;
        }

        saving = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, _, error) = IsNew
                ? await Http.ExecuteRequestAsync<ApiResponse<string>>(HttpVerb.Post, "api/organisations", organisation)
                : await Http.ExecuteRequestAsync<ApiResponse<string>>(HttpVerb.Patch, $"api/organisations/{EntityId}", organisation);

            if (!ok)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to save the organisation.", Severity.Error);
                return;
            }

            Snackbar.Add(IsNew ? "Organisation created." : "Organisation updated.", Severity.Success);
            Nav.NavigateTo("/settings/organisations");
        }
        finally
        {
            saving = false;
        }
    }
}
