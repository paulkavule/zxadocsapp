using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocsui.Srevices;

namespace zxadocsui.Components.Pages.Dashboard.Accounting;

/// <summary>Adds or edits a GL account in the side panel; closes with true when it saved. The API validates.</summary>
public partial class GlAccountEditor
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    [Parameter] public GlAccountDto Account { get; set; } = new();
    [Parameter] public List<GlAccountDto> Parents { get; set; } = new();

    private bool busy;

    private async Task Save()
    {
        busy = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, _, error) = Account.Id == 0
                ? await Http.ExecuteRequestAsync<ApiResponse<GlAccountDto>>(HttpVerb.Post, "api/accounting/accounts", Account)
                : await Http.ExecuteRequestAsync<ApiResponse<GlAccountDto>>(HttpVerb.Put, $"api/accounting/accounts/{Account.Id}", Account);

            if (!ok)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to save the account.", Severity.Error);
                return;
            }

            Snackbar.Add(Account.Id == 0 ? "Account added." : "Account updated.", Severity.Success);
            SideDialog.Close(true);
        }
        finally
        {
            busy = false;
        }
    }
}
