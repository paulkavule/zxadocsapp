using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Accounting;

/// <summary>Posted journals with their lines (ZD-215), newest first.</summary>
public partial class JournalList
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private List<JournalDto> journals = new();
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

    private string ReversalText(JournalDto journal) =>
        journal.ReversesJournalId is { } original ? $"reverses {Number(original)}"
        : journal.ReversedByJournalId is { } by ? $"reversed by {Number(by)}"
        : "";

    private string Number(int id) => journals.FirstOrDefault(j => j.Id == id)?.Number ?? $"#{id}";
}
