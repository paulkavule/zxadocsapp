using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.Srevices;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Accounting;

/// <summary>Posted journals with their lines (ZD-215), newest first. ReverseJournal holders can reverse one (ZD-235).</summary>
public partial class JournalList
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    private List<JournalDto> journals = new();
    private bool canReverse;
    private bool loading = true;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        if (!await Permissions.HasAny(Permission.ManageAccounting, Permission.ReverseJournal, Permission.ViewPettyCashFinance))
        {
            Snackbar.Add("You do not have access to journals.", Severity.Warning);
            Nav.NavigateTo("/dashboard");
            return;
        }

        canReverse = await Permissions.Has(Permission.ReverseJournal);
        await Load();
        StateHasChanged();
    }

    private async Task Load()
    {
        loading = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, result, error) = await Http.GetAsync<ApiResponse<List<JournalDto>>>("api/accounting/journals");
            if (!ok || result?.Data is null)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to load journals.", Severity.Error);
                return;
            }
            journals = result.Data;
        }
        finally
        {
            loading = false;
        }
    }

    private async Task Reverse(JournalDto journal)
    {
        var posted = await SideDialog.Show<JournalReverseEditor, bool?>(
            new Dictionary<string, object> { { nameof(JournalReverseEditor.Journal), journal } },
            title: $"Reverse {journal.Number}");

        if (posted == true)
        {
            await Load();
            StateHasChanged();
        }
    }

    private string ReversalText(JournalDto journal) =>
        journal.ReversesJournalId is { } original ? $"reverses {Number(original)}"
        : journal.ReversedByJournalId is { } by ? $"reversed by {Number(by)}"
        : "";

    private string Number(int id) => journals.FirstOrDefault(j => j.Id == id)?.Number ?? $"#{id}";
}
