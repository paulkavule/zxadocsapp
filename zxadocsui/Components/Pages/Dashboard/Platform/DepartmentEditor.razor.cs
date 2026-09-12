using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocsui.Srevices;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Platform;

/// <summary>
/// Add or rename a department, in the side panel rather than inline on the list. Closes with true
/// when it saved, so the list behind it knows whether to reload.
/// </summary>
public partial class DepartmentEditor
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;
    [Inject] private ActingOrganisationState Acting { get; set; } = default!;

    /// <summary>Zero for a new department.</summary>
    [Parameter] public int DepartmentId { get; set; }

    [Parameter] public string DepartmentName { get; set; } = string.Empty;

    [Parameter] public List<ListOption> Organisations { get; set; } = new();

    /// <summary>False for an organisation's own administrator, who has no organisation to choose.</summary>
    [Parameter] public bool SystemUser { get; set; }

    private string name = string.Empty;
    private bool busy;

    private bool IsNew => DepartmentId == 0;

    private bool ChoosesOrganisation => SystemUser && IsNew;

    private bool NeedsOrganisation =>
        SystemUser && (Acting.IsAllOrganisations || Acting.OrganisationId is not > 0);

    private int? SelectedOrganisationId => Acting.IsAllOrganisations ? null : Acting.OrganisationId;

    protected override void OnInitialized() => name = DepartmentName;

    /// <summary>
    /// Writes through the acting state rather than holding its own copy, so the list behind the
    /// panel and the record being created can never disagree about the organisation.
    /// </summary>
    private async Task ChooseOrganisation(int? organisationId)
    {
        if (organisationId is not > 0) return;

        var label = Organisations.FirstOrDefault(o => o.Id == organisationId)?.Name ?? string.Empty;
        await Acting.Select(organisationId, label);
        StateHasChanged();
    }

    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Snackbar.Add("A department needs a name.", Severity.Warning);
            return;
        }

        busy = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var body = new { Name = name.Trim(), Description = string.Empty };
            var (ok, _, error) = IsNew
                ? await Http.ExecuteRequestAsync<ApiResponse<int>>(HttpVerb.Post, "api/departments", body)
                : await Http.ExecuteRequestAsync<ApiResponse<int>>(HttpVerb.Put, $"api/departments/{DepartmentId}", body);

            if (!ok)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to save the department.", Severity.Error);
                return;
            }

            Snackbar.Add(IsNew ? "Department added." : "Department updated.", Severity.Success);
            SideDialog.Close(true);
        }
        finally
        {
            busy = false;
        }
    }
}
