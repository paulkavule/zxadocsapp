using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocsui.Srevices;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Platform;

/// <summary>One step of the document's workflow. Mirrors the API's shape.</summary>
public record WorkflowActor
{
    public int Level { get; set; }
    public int UserId { get; set; }
    public string User { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string ExpectedRole { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
}

/// <summary>
/// Hand a document to a different actor, in the side panel rather than inline on the list. Closes
/// with true when it reassigned, so the list behind it knows whether to reload.
/// </summary>
public partial class WorkflowReassign
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;
    [Inject] private ActingOrganisationState Acting { get; set; } = default!;

    [Parameter] public int DocumentId { get; set; }

    [Parameter] public string Title { get; set; } = string.Empty;

    /// <summary>Whoever holds it now, excluded from the candidates: the API refuses a no-op move.</summary>
    [Parameter] public int NextActorId { get; set; }

    [Parameter] public string NextActorName { get; set; } = string.Empty;

    private List<ListOption> candidates = new();
    private List<WorkflowActor> actors = new();
    private int targetUserId;
    private bool busy;
    private bool loaded;

    protected override async Task OnInitializedAsync()
    {
        Http.Initialize(AppConstants.HttpSchemes.Core);

        // The endpoint takes the organisation from the token, which is the acting one for a
        // system user; the route id only selects the list type.
        var orgId = Acting.OrganisationId ?? 0;
        var (ok, result, _) = await Http
            .GetAsync<ApiResponse<List<ListOption>>>($"api/listoptions/{orgId}?type=usersearch");

        if (ok && result?.Data is not null)
            candidates = result.Data
                .Where(user => user.Id != NextActorId)
                .OrderBy(user => user.Name)
                .ToList();

        var (gotActors, actorResult, _) = await Http
            .GetAsync<ApiResponse<List<WorkflowActor>>>($"api/support/workflows/{DocumentId}/actors");

        if (gotActors && actorResult?.Data is not null)
            actors = actorResult.Data;

        loaded = true;
    }

    private async Task Confirm()
    {
        if (targetUserId == 0) return;

        busy = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, _, error) = await Http.ExecuteRequestAsync<ApiResponse<int>>(
                HttpVerb.Post, $"api/support/workflows/{DocumentId}/reassign",
                new { UserId = targetUserId });

            if (!ok)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to reassign.", Severity.Error);
                return;
            }

            Snackbar.Add("Workflow reassigned.", Severity.Success);
            SideDialog.Close(true);
        }
        finally
        {
            busy = false;
        }
    }
}
