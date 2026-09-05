using System;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Dtos;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard;

public partial class Documents
{
    [Inject] ILogger<CreateWorkflow>? Logger { get; set; } = default!;
    [Inject] IHttpService? HttpSvc { get; set; } = default!;
    [Inject] IDialogService? DialogService { get; set; }
    [Inject] ISnackbar? Snackbar { get; set; }
    [Inject] IUserSession? Session { get; set; }
    [Parameter] public int? LoadedTabsId { get; set; }
    [Inject] NavigationManager Navigator { set; get; } = default!;
    List<QueryDto.DocumentQuery> inboxList = new(), outboxList = new(), archievedList = new(), deletedList = new(), draftList = new();

    UserData userData = new();

    // ?tab=inbox|outbox|archived|deleted — lets other pages deep-link to a folder, e.g. the
    // dashboard's "View all" pending-approvals button. Unknown or absent means Inbox.
    [SupplyParameterFromQuery(Name = "tab")] public string? Tab { get; set; }

    int activeTab;
    bool tabApplied;
    string? appliedTab;
    bool loaded;

    // Apply the query string only when it actually changes. Re-applying on every parameter set
    // would snap the user back to the URL's tab after they had clicked a different one.
    protected override async Task OnParametersSetAsync()
    {
        if (tabApplied && appliedTab == Tab) return;

        tabApplied = true;
        appliedTab = Tab;
        activeTab = TabIndex(Tab);
        if(LoadedTabsId.HasValue)
        {
            activeTab = LoadedTabsId.Value;
        }

        // Before the first render there is no session/token yet; OnAfterRenderAsync does that load.
        if (loaded) await LoadDocuments(FolderFor(activeTab));
    }

    private static int TabIndex(string? tab) => (tab ?? string.Empty).ToLowerInvariant() switch
    {
        "outbox" => 1,
        "archived" or "archieved" => 2,
        "deleted" => 3,
        _ => 0,      // inbox
    };

    private static DocStatus FolderFor(int tabIndex) => tabIndex switch
    {
        1 => DocStatus.Outbox,
        2 => DocStatus.Archived,
        3 => DocStatus.Deleted,
        _ => DocStatus.Published,
    };

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            userData = await Session!.GetCurrentUser();
            HttpSvc?.Initialize(AppConstants.HttpSchemes.Core);
            // Load whichever folder the query string selected, not always the Inbox.
            await LoadDocuments(FolderFor(activeTab));
            loaded = true;
            StateHasChanged();
        }
    }
    private async Task CreateDocument()
    {
        Navigator.NavigateTo("/newdocument");
    }

    private async Task LoadDocuments(DocStatus folder)
    {
        try
        {
            var (status, result, message) = await HttpSvc!.GetAsync<ApiResponse<List<QueryDto.DocumentQuery>>>($"/api/documents/dashboard?id={(int)folder}&userName={userData.UserId}&pageNumber=1&pageSize=100");
            if (status == false)
                return;
            if (!status || result == null || result.Data.Count <= 0)
                return;
            switch (folder)
            {
                case DocStatus.Published:
                    inboxList = result.Data.Where(doc => doc.NextActorId == int.Parse(userData.UserId) && doc.Status != DocStatus.Archived).ToList();
                    break;
                case DocStatus.Archived:
                    archievedList = result.Data.Where(doc => doc.NextActorId != int.Parse(userData.UserId) && doc.Status == DocStatus.Archived).ToList();
                    break;
                case DocStatus.Outbox:
                    outboxList = result.Data.Where(doc => doc.Author.Id == int.Parse(userData.UserId)).ToList();
                    break;
                case DocStatus.Deleted:
                    deletedList = result.Data.Where(doc => doc.Status == DocStatus.Deleted).ToList();
                    break;
                case DocStatus.Draft:
                    draftList = result.Data.Where(doc => doc.Status == DocStatus.Draft).ToList();
                    break;
            }

            // outboxList = result.Data.Where(doc => doc.Status == (int)DocStatus.Outbox).ToList();

            // deletedList = result.Data.Where(doc => doc.Status == (int)DocStatus.Deleted).ToList();

            Console.WriteLine("Access Token: " + result.Data.Count);
        }
        catch (Exception ee)
        {
            Snackbar!.Clear();
            Snackbar.Add($"Faile to load documents {ee.Message}", Severity.Error);
        }
    }

    async Task IndexChanged(int page)
    {
        // ActivePanelIndex is bound, so the click has to be recorded here or the tab reverts.
        activeTab = page;
        await LoadDocuments(FolderFor(page));
    }
}
