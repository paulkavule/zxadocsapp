using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
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

    private List<SupportWorkflow> workflows = new();
    private List<ListOption> candidates = new();
    private SupportWorkflow? reassigning;
    private int targetUserId;
    private bool loading = true;
    private bool busy;
    private bool canReassign;

    protected override void OnInitialized() => Acting.Changed += OnActingChanged;

    public void Dispose() => Acting.Changed -= OnActingChanged;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        var roles = await Permissions.GetSystemRoles();
        if (!roles.Contains(SystemRole.SystemSupport) && !roles.Contains(SystemRole.SystemViewer))
        {
            Snackbar.Add("This page requires the System Support or System Viewer role.", Severity.Warning);
            Nav.NavigateTo("/dashboard");
            return;
        }

        canReassign = roles.Contains(SystemRole.SystemSupport);
        await Load();
        StateHasChanged();
    }

    private async Task OnActingChanged()
    {
        CancelReassign();
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

    private async Task BeginReassign(SupportWorkflow workflow)
    {
        reassigning = workflow;
        targetUserId = 0;
        await LoadCandidates();
    }

    private void CancelReassign()
    {
        reassigning = null;
        targetUserId = 0;
        candidates.Clear();
    }

    /// <summary>
    /// The organisation's users, from the search list option with an empty term. The endpoint takes
    /// the organisation from the token, which is the acting one for a system user.
    /// </summary>
    private async Task LoadCandidates()
    {
        candidates.Clear();
        Http.Initialize(AppConstants.HttpSchemes.Core);

        var orgId = Acting.OrganisationId ?? 0;
        var (ok, result, _) = await Http
            .GetAsync<ApiResponse<List<ListOption>>>($"api/listoptions/{orgId}?type=usersearch");

        if (ok && result?.Data is not null)
            candidates = result.Data
                .Where(user => user.Id != reassigning?.NextActorId)
                .OrderBy(user => user.Name)
                .ToList();
    }

    private async Task ConfirmReassign()
    {
        if (reassigning is null || targetUserId == 0) return;

        busy = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, _, error) = await Http.ExecuteRequestAsync<ApiResponse<int>>(
                HttpVerb.Post, $"api/support/workflows/{reassigning.DocumentId}/reassign",
                new { UserId = targetUserId });

            if (!ok)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to reassign.", Severity.Error);
                return;
            }

            Snackbar.Add("Workflow reassigned.", Severity.Success);
            CancelReassign();
            await Load();
        }
        finally
        {
            busy = false;
        }
    }
}
