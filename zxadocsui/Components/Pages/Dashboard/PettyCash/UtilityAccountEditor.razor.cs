using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocsui.Srevices;

namespace zxadocsui.Components.Pages.Dashboard.PettyCash;

/// <summary>Adds or edits a utility account in the side panel; closes with true when it saved. The API validates.</summary>
public partial class UtilityAccountEditor
{
    private static readonly string[] Types = ["Electricity", "Water"];

    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    [Parameter] public UtilityAccountDto Account { get; set; } = new();

    private List<BillingUnitDto> units = new();
    private bool busy;

    protected override async Task OnInitializedAsync()
    {
        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (ok, result, _) = await Http.GetAsync<ApiResponse<List<BillingUnitDto>>>("api/petty-cash/billing-units");
        units = ok && result?.Data is not null ? result.Data : new();
    }

    private Task<IEnumerable<string>> SearchTypes(string value, CancellationToken _) =>
        Task.FromResult(Types.Where(t => string.IsNullOrWhiteSpace(value) || t.Contains(value, StringComparison.OrdinalIgnoreCase)));

    private async Task Save()
    {
        busy = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, _, error) = Account.Id == 0
                ? await Http.ExecuteRequestAsync<ApiResponse<UtilityAccountDto>>(HttpVerb.Post, "api/petty-cash/utility-accounts", Account)
                : await Http.ExecuteRequestAsync<ApiResponse<UtilityAccountDto>>(HttpVerb.Put, $"api/petty-cash/utility-accounts/{Account.Id}", Account);

            if (!ok)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to save the utility account.", Severity.Error);
                return;
            }

            Snackbar.Add(Account.Id == 0 ? "Utility account added." : "Utility account updated.", Severity.Success);
            SideDialog.Close(true);
        }
        finally
        {
            busy = false;
        }
    }
}
