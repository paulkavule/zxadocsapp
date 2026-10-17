using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocsui.Srevices;

namespace zxadocsui.Components.Pages.Dashboard.Accounting;

/// <summary>Adds an accounting period in the side panel; closes with true when it saved. The API refuses overlaps.</summary>
public partial class AccountingPeriodEditor
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    private string name = string.Empty;
    private DateTime? start;
    private DateTime? end;
    private bool busy;

    private async Task Save()
    {
        busy = true;
        try
        {
            var period = new AccountingPeriodDto
            {
                Name = name, StartDate = DateOnly.FromDateTime(start!.Value), EndDate = DateOnly.FromDateTime(end!.Value),
            };
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, _, error) = await Http.ExecuteRequestAsync<ApiResponse<AccountingPeriodDto>>(HttpVerb.Post, "api/accounting/periods", period);

            if (!ok)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to add the period.", Severity.Error);
                return;
            }

            Snackbar.Add("Period added.", Severity.Success);
            SideDialog.Close(true);
        }
        finally
        {
            busy = false;
        }
    }
}
