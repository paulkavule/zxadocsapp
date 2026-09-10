using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocsui.Srevices;

namespace zxadocsui.Components.Pages.Dashboard.Platform;

/// <summary>
/// Create or edit a tenant, in the side panel rather than on its own route. Keyed on EntityId
/// rather than an int id, because that is the handle the organisation endpoints take. Closes with
/// true when it saved, so the list behind it knows whether to reload.
///
/// The System Admin gate lives on the list, which is the only thing that opens this; the
/// endpoints re-check it regardless.
/// </summary>
public partial class OrganisationEditor
{
    /// <summary>Empty for a new organisation.</summary>
    [Parameter] public string EntityId { get; set; } = string.Empty;

    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    private Organisation organisation = new();
    private bool loading;
    private bool saving;

    private bool IsNew => string.IsNullOrWhiteSpace(EntityId);

    protected override async Task OnInitializedAsync()
    {
        if (IsNew) return;

        loading = true;
        try
        {
            await LoadOrganisation();
        }
        finally
        {
            loading = false;
        }
    }

    private async Task LoadOrganisation()
    {
        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (ok, result, error) = await Http
            .GetAsync<ApiResponse<Organisation>>($"api/organisations/{EntityId}");

        if (!ok || result?.Data is null)
        {
            Snackbar.Add(ErrorMessage.Extract(error) ?? "That organisation could not be found.", Severity.Warning);
            SideDialog.Close(false);
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
            SideDialog.Close(true);
        }
        finally
        {
            saving = false;
        }
    }
}
