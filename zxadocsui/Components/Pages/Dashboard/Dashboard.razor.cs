using System;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using zxadocsfe.Dtos;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.Components.DocWorkflow;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard;

public partial class Dashboard : IDisposable
{

    [Inject] ISnackbar Snackbar { set; get; } = default!;
    [Inject] ILogger<DocumentEditor> logger { set; get; } = default!;
    [Inject] IDialogService dialog { set; get; } = default!;
    [Inject] IHttpService httpSvc { get; set; } = default!;
    [Inject] IUserSession session { get; set; } = default!;
    [Inject] IJSRuntime JSRuntime { set; get; } = default!;
    [Inject] NavigationManager navigator { set; get; } = default!;
    [Inject] IActivityClientService ActivityApi { set; get; } = default!;
    [Inject] IPermissionClientService permissions { set; get; } = default!;
    [Inject] ActingOrganisationState acting { set; get; } = default!;
    StatisticsDto statistics = new();
    UserData userData = new();
    List<ListValue> listValues = new();

    // Documents waiting on THIS user, i.e. the Documents page's Inbox. The card shows the first
    // few; the button carries the full count.
    List<QueryDto.DocumentQuery> pendingApprovals = new();
    const int PendingApprovalsShown = 3;

    // The card's link into the activity report, which now needs reporting access (ZD-133).
    bool canReports;
    protected override void OnInitialized()
    {
        httpSvc.Initialize(AppConstants.HttpSchemes.Core);
        acting.Changed += OnActingChanged;
    }

    public void Dispose() => acting.Changed -= OnActingChanged;

    /// <summary>
    /// The tiles, the recent activity and the chart are all organisation-scoped, so a system user
    /// switching tenant in the header has to see them follow. The pending-approvals card is this
    /// user's own inbox and does not move.
    /// </summary>
    private async Task OnActingChanged()
    {
        statistics = new();
        listValues.Clear();
        actionMix.Clear();

        // Merged across tenants these say nothing, so the server refuses "all organisations"
        // and there is nothing to ask for.
        if (!acting.IsAllOrganisations)
        {
            await GetDashboardStats();
            await GetRecentActivity();
            await GetActionMix();
        }

        await InvokeAsync(StateHasChanged);
    }
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        Console.WriteLine("-- -- -- -- -- -- >1");
        if (firstRender)
        {

            await LoadUserInformation();

            canReports = (await permissions.GetPermissions()).Contains(Permission.ViewOrganisationReports)
                      || (await permissions.GetSystemRoles()).Contains(SystemRole.SystemViewer);

            await GetDashboardStats();
            await GetRecentActivity();
            await GetPendingApprovals();
            await GetActionMix();

            StateHasChanged();
        }

    }

    private async Task LoadUserInformation()
    {
        userData = await session!.GetCurrentUser();
        if (userData == null)
        {
            Snackbar.Clear();
            Snackbar.Add("Couldn't retrieve user details. Signature not intialized", Severity.Error);
        }
    }

    async Task GetDashboardStats()
    {
        var (sucess, result, message) = await httpSvc!.GetAsync<ApiResponse<StatisticsDto>>($"/statistics/summary?organisationId={userData.EntityId}");
        if (sucess == false)
        {
            Snackbar!.Clear();
            Snackbar!.Add(message ?? "Error on loading statistical data", Severity.Warning);
            return;
        }

        statistics = result!.Data;
    }

    async Task GetRecentActivity()
    {
        var (sucess, result, message) = await httpSvc!.GetAsync<ApiResponse<List<ListValue>>>($"/statistics/recent-activity?organisationId={userData.EntityId}");
        if (sucess == false)
        {
            Snackbar!.Clear();
            Snackbar!.Add(message ?? "Error on loading statistical data", Severity.Warning);
            return;
        }

        listValues = result!.Data;
    }
    // Same source and filter as the Documents page's Inbox tab, so the count on this card and the
    // list the "View all" button lands on can never disagree.
    async Task GetPendingApprovals()
    {
        if (!int.TryParse(userData.UserId, out var userId)) return;

        var (success, result, message) = await httpSvc!.GetAsync<ApiResponse<List<QueryDto.DocumentQuery>>>(
            $"/api/documents/dashboard?id={(int)DocStatus.Published}&userName={userId}&pageNumber=1&pageSize=100");

        if (!success || result?.Data is null)
        {
            Snackbar!.Clear();
            Snackbar!.Add(message ?? "Error loading your pending approvals", Severity.Warning);
            return;
        }

        pendingApprovals = result.Data
            .Where(doc => doc.NextActorId == userId && doc.Status != DocStatus.Archived)
            .OrderBy(doc => doc.DueDate ?? DateTime.MaxValue)   // soonest due first; undated last
            .ToList();
    }

    // ---- Approval aging (diverging) -----------------------------------------------------
    //
    // Diverging because "overdue vs still-to-come" is polarity around a baseline, not eight
    // unrelated categories. Poles are the palette's diverging pair (red ↔ blue) with a gray
    // neutral at "due today"; validated on the white card surface — worst pair CVD ΔE 21.6,
    // normal-vision 32.3, both poles clear 3:1.
    internal const string OverdueColor = "#e34948";
    internal const string DueTodayColor = "#898781";
    internal const string UpcomingColor = "#2a78d6";

    internal sealed record AgingBucket(string Label, int Count, int Side, string Color);

    // Side: -1 overdue (left of centre), 0 due today, +1 upcoming (right).
    private List<AgingBucket> AgingBuckets()
    {
        var today = DateTime.Today;
        int Count(Func<int, bool> match) => pendingApprovals
            .Count(d => d.DueDate is not null && match((d.DueDate.Value.Date - today).Days));

        return new List<AgingBucket>
        {
            new("7+ days overdue",  Count(d => d <= -8),           -1, OverdueColor),
            new("4-7 days overdue", Count(d => d is >= -7 and <= -4), -1, OverdueColor),
            new("1-3 days overdue", Count(d => d is >= -3 and <= -1), -1, OverdueColor),
            new("Due today",        Count(d => d == 0),             0, DueTodayColor),
            new("Due in 1-3 days",  Count(d => d is >= 1 and <= 3),  1, UpcomingColor),
            new("Due in 4-7 days",  Count(d => d is >= 4 and <= 7),  1, UpcomingColor),
            new("Due in 7+ days",   Count(d => d >= 8),              1, UpcomingColor),
        };
    }

    // Bars are scaled against the busiest bucket, so the widest is always readable.
    private static int AgingScale(IEnumerable<AgingBucket> buckets)
    {
        var max = buckets.Max(b => b.Count);
        return max <= 0 ? 1 : max;
    }

    private int UndatedApprovals => pendingApprovals.Count(d => d.DueDate is null);

    // Returns a STRING, invariant: a double interpolated into a style attribute formats with
    // CurrentCulture, so a comma-decimal host emits "width:57,14%" and the browser drops the
    // declaration — the bar silently disappears.
    internal static string Pct(int count, int scale) =>
        (count * 100.0 / (scale <= 0 ? 1 : scale)).ToString("0.##", CultureInfo.InvariantCulture);

    // ---- Action mix (stacked, last 7 days) ----------------------------------------------
    //
    // Categorical slots 1-3 in fixed order; "Other" takes the de-emphasis gray rather than a
    // fourth hue. Validated on white: worst pair CVD ΔE 8.4, normal-vision 21.6, all >= 3:1.
    internal static readonly (string Label, string Color)[] ActionSeries =
    {
        ("Created",  "#2a78d6"),
        ("Rejected", "#eb6834"),
        ("Approved", "#199e70"),
        ("Other",    "#898781"),
    };

    // day -> series label -> count
    private readonly List<(DateTime Day, Dictionary<string, int> Counts)> actionMix = new();

    async Task GetActionMix()
    {
        var (ok, rows, error) = await ActivityApi.GetDailyActionMix(7);
        if (!ok)
        {
            Snackbar!.Clear();
            Snackbar!.Add(error ?? "Error loading the action mix", Severity.Warning);
            return;
        }

        actionMix.Clear();
        foreach (var group in rows.GroupBy(r => r.Key).OrderBy(g => g.Key))
        {
            if (!DateTime.TryParse(group.Key, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var day))
                continue;

            var counts = ActionSeries.ToDictionary(s => s.Label, _ => 0);
            foreach (var row in group)
            {
                if (string.IsNullOrWhiteSpace(row.Name)) continue;   // empty-day marker
                counts[SeriesFor(row.Name)] += int.TryParse(row.Value, out var n) ? n : 0;
            }
            actionMix.Add((day, counts));
        }
    }

    // ActionHistory stores "Created" plus the ApprovalStatus names. Anything outside the three
    // headline actions folds into Other rather than growing the palette.
    private static string SeriesFor(string action) => action.Trim().ToLowerInvariant() switch
    {
        "created" => "Created",
        "reject" or "rejected" => "Rejected",
        "approve" or "approved" => "Approved",
        _ => "Other",
    };

    private int ActionMixMax => actionMix.Count == 0
        ? 1
        : Math.Max(1, actionMix.Max(d => d.Counts.Values.Sum()));

    void ViewAllPendingApprovals() => navigator.NavigateTo("/documents?tab=inbox");

    void OpenDocument(int documentId) => navigator.NavigateTo($"/viewdocument/{documentId}");

    // Amber once it is due today or overdue; plain grey while there is still time.
    static (string Text, bool Urgent) DueLabel(DateTime? dueDate)
    {
        if (dueDate is null) return ("no due date", false);

        var days = (dueDate.Value.Date - DateTime.Today).Days;
        return days switch
        {
            < 0 => ($"{Math.Abs(days)}d overdue", true),
            0 => ("due today", true),
            1 => ("due tomorrow", false),
            _ => ($"in {days}d", false),
        };
    }

    void Clicked()
    {
        Console.WriteLine("This is okay");
    }

}
