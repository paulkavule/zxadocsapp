using System.Text.Json;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard;

// Audit trail (/audit). Server-paginated MudTable over GET /api/audit, filtered by entity
// type, action, entity id and actor. Gated on ViewAudit — the server enforces it; the check
// here only decides between the table and the "no permission" message.
public partial class Audit
{
    [Inject] private IAuditClientService AuditApi { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    // The actions AuditWriter records today (TemplateService / DraftService call sites).
    private static readonly string[] KnownActions =
    {
        "create", "submit", "approve", "reject", "download",
        "archive", "upload-version", "generate-for-signing", "reassign",
    };

    private MudTable<AuditEntryDto>? table;
    private readonly HashSet<int> expanded = new();

    private string entityTypeFilter = string.Empty;
    private string actionFilter = string.Empty;
    private string entityIdFilter = string.Empty;
    private string actorFilter = string.Empty;

    private bool canView;
    private bool checkedPermission;

    private bool HasFilters =>
        !string.IsNullOrWhiteSpace(entityTypeFilter) || !string.IsNullOrWhiteSpace(actionFilter)
        || !string.IsNullOrWhiteSpace(entityIdFilter) || !string.IsNullOrWhiteSpace(actorFilter);

    // Permission check runs after first render so the token is hydrated; in OnInitializedAsync
    // the tokenless call 401s and the page would claim the user lacks ViewAudit.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        await Session.GetCurrentUser();
        canView = await Permissions.Has(Permission.ViewAudit);
        checkedPermission = true;
        StateHasChanged();
        if (canView) await Reload();
    }

    private async Task<TableData<AuditEntryDto>> LoadData(TableState state, CancellationToken token)
    {
        // A part-typed or non-numeric entity id filters nothing rather than erroring.
        _ = int.TryParse(entityIdFilter, out var entityId);

        // MudTable pages are 0-based; the API is 1-based.
        var (ok, page, error) = await AuditApi.List(
            entityTypeFilter, entityId, actionFilter, actorFilter, state.Page + 1, state.PageSize);

        if (!ok || page is null)
        {
            Snackbar.Add(error ?? "Failed to load the audit trail.", Severity.Error);
            return new TableData<AuditEntryDto> { Items = Array.Empty<AuditEntryDto>(), TotalItems = 0 };
        }

        return new TableData<AuditEntryDto> { Items = page.Data, TotalItems = page.TotalCount };
    }

    private Task Reload()
    {
        // Ids are not stable across pages/filters, so a stale expansion would open the wrong row.
        expanded.Clear();
        return table?.ReloadServerData() ?? Task.CompletedTask;
    }

    private async Task OnEntityTypeChanged(string value) { entityTypeFilter = value; await Reload(); }
    private async Task OnActionChanged(string value) { actionFilter = value; await Reload(); }
    private async Task OnEntityIdChanged(string value) { entityIdFilter = value; await Reload(); }
    private async Task OnActorChanged(string value) { actorFilter = value; await Reload(); }

    private async Task ClearFilters()
    {
        entityTypeFilter = string.Empty;
        actionFilter = string.Empty;
        entityIdFilter = string.Empty;
        actorFilter = string.Empty;
        await Reload();
    }

    private void ToggleRow(int id)
    {
        if (!expanded.Remove(id)) expanded.Add(id);
    }

    private string ToggleIcon(int id) =>
        expanded.Contains(id) ? Icons.Material.Filled.ExpandLess : Icons.Material.Filled.ExpandMore;

    // "{}" is the writer's "nothing to record" value, so those rows get no expander.
    private static bool HasMetadata(AuditEntryDto entry) =>
        !string.IsNullOrWhiteSpace(entry.Metadata)
        && entry.Metadata.Trim() is not ("{}" or "null");

    // Metadata is free-form jsonb (rejection reason, document reference, file hash). Render
    // whatever top-level keys it has; malformed JSON falls back to the raw string rather than
    // throwing inside the render tree.
    private static IEnumerable<(string Key, string Value)> MetadataPairs(string metadata)
    {
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(metadata).RootElement;
        }
        catch (JsonException)
        {
            return new[] { ("metadata", metadata) };
        }

        if (root.ValueKind != JsonValueKind.Object)
            return new[] { ("metadata", root.ToString()) };

        return root.EnumerateObject()
            .Select(p => (p.Name, p.Value.ValueKind == JsonValueKind.String
                ? p.Value.GetString() ?? string.Empty
                : p.Value.ToString()))
            .ToList();
    }

    private static Color ActionColor(string action) => action switch
    {
        "approve" => Color.Success,
        "reject" => Color.Error,
        "submit" => Color.Warning,
        "create" => Color.Info,
        "archive" => Color.Dark,
        _ => Color.Default,
    };
}
