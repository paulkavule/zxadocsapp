using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.Srevices;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Platform;

/// <summary>One document and who currently has to act on it. Mirrors the API's shape (ZD-132).</summary>
public record SupportWorkflow
{
    public int DocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public int NextActorId { get; set; }
    public string NextActor { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }
    public DateTime? CreatedAt { get; set; }
}

// Support administration (ZD-132). System Viewer can look; only System Support can reassign.
public partial class SupportWorkflowList : IDisposable
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private ActingOrganisationState Acting { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    private List<SupportWorkflow> workflows = new();
    private bool loading = true;
    private bool canReassign;

    protected override void OnInitialized() => Acting.Changed += OnActingChanged;

    public void Dispose() => Acting.Changed -= OnActingChanged;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        var roles = await Permissions.GetSystemRoles();
        // The organisation reaches this through the permission; the platform through its roles.
        canReassign = await Permissions.Has(Permission.ProcessReassignment)
                   || roles.Contains(SystemRole.SystemSupport);

        if (!canReassign && !roles.Contains(SystemRole.SystemViewer))
        {
            Snackbar.Add("This page requires reassignment access.", Severity.Warning);
            Nav.NavigateTo("/dashboard");
            return;
        }
        await Load();
        StateHasChanged();
    }

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
            workflows.Clear();
            if (Acting.IsAllOrganisations)
                return;

            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, result, error) = await Http
                .GetAsync<ApiResponse<List<SupportWorkflow>>>("api/support/workflows");

            if (!ok || result?.Data is null)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to load workflows.", Severity.Error);
                return;
            }

            workflows = result.Data;
        }
        finally
        {
            loading = false;
        }
    }

    /// <summary>Reassignment lives in the side panel; it reports back whether it actually moved.</summary>
    private async Task OpenReassign(SupportWorkflow workflow)
    {
        var reassigned = await SideDialog.Show<WorkflowReassign, bool?>(
            new Dictionary<string, object>
            {
                { nameof(WorkflowReassign.DocumentId), workflow.DocumentId },
                { nameof(WorkflowReassign.Title), workflow.Title },
                { nameof(WorkflowReassign.NextActorId), workflow.NextActorId },
                { nameof(WorkflowReassign.NextActorName), workflow.NextActor },
            },
            title: "Reassign workflow",
            width: 520);

        if (reassigned == true)
            await Load();
    }
}
