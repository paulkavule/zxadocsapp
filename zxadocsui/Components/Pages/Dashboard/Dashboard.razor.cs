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

public partial class Dashboard
{

    [Inject] ISnackbar Snackbar { set; get; } = default!;
    [Inject] ILogger<DocumentEditor> logger { set; get; } = default!;
    [Inject] IDialogService dialog { set; get; } = default!;
    [Inject] IHttpService httpSvc { get; set; } = default!;
    [Inject] IUserSession session { get; set; } = default!;
    [Inject] IJSRuntime JSRuntime { set; get; } = default!;
    [Inject] NavigationManager navigator { set; get; } = default!;
    StatisticsDto statistics = new();
    UserData userData = new();
    List<ListValue> listValues = new(), weeklyStats = new();

    // Documents waiting on THIS user, i.e. the Documents page's Inbox. The card shows the first
    // few; the button carries the full count.
    List<QueryDto.DocumentQuery> pendingApprovals = new();
    const int PendingApprovalsShown = 3;
    protected override void OnInitialized()
    {
        httpSvc.Initialize(AppConstants.HttpSchemes.Core);
    }
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        Console.WriteLine("-- -- -- -- -- -- >1");
        if (firstRender)
        {

            await LoadUserInformation();
            await GetDashboardStats();
            await GetRecentActivity();
            await GetWeeklyStatistic();
            await GetPendingApprovals();

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

    async Task GetWeeklyStatistic()
    {
        int weekNumber = ISOWeek.GetWeekOfYear(DateTime.Today);
        var (sucess, result, message) = await httpSvc!.GetAsync<ApiResponse<List<ListValue>>>($"/statistics/weekly/summary?organisationId={userData.EntityId}&week={weekNumber}");
        if (sucess == false || result == null || result?.Data == null)
        {
            Snackbar!.Clear();
            Snackbar!.Add("Error on loading weekly statistical data", Severity.Warning);
            return;
        }

        weeklyStats = result?.Data!;
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
    public static string GetBarClasses(string? value)
    {
        var score = int.TryParse(value, out var v) ? v : 1;

        var colorClass = score switch
        {
            >= 50 => "bg-green-500",
            < 50 and > 40 => "bg-green-400",
            < 40 and > 30 => "bg-green-300",
            < 30 and > 10 => "bg-green-200",
            _ => "bg-green-100"
        };

        var heightClass = score switch
        {
            >= 50 => "h-32",
            < 50 and > 40 => "h-24",
            < 40 and > 30 => "h-20",
            < 30 and >= 1 => "h-10",
            _ => "h-0"
        };

        return $"w-1/5 rounded-t-lg {colorClass} {heightClass}";
    }

}
