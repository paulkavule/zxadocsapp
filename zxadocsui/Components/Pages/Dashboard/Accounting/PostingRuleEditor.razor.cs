using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocsui.Srevices;

namespace zxadocsui.Components.Pages.Dashboard.Accounting;

/// <summary>Adds a posting rule in the side panel; closes with true when it saved. Offers only accounts that can post.</summary>
public partial class PostingRuleEditor
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    private PostingRuleDto rule = new();
    private List<GlAccountDto> accounts = new();
    private List<SpendingCategoryDto> categories = new();
    private DateTime? from = DateTime.Today;
    private bool busy;

    protected override async Task OnInitializedAsync()
    {
        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (ok, result, _) = await Http.GetAsync<ApiResponse<List<GlAccountDto>>>("api/accounting/accounts");
        accounts = ok && result?.Data is not null ? result.Data.Where(a => a.IsActive && a.AllowPosting).ToList() : new();
        var (categoriesOk, categoryList, _) = await Http.GetAsync<ApiResponse<List<SpendingCategoryDto>>>("api/petty-cash/categories");
        categories = categoriesOk && categoryList?.Data is not null ? categoryList.Data : new();
    }

    private async Task Save()
    {
        busy = true;
        try
        {
            rule.EffectiveFrom = DateOnly.FromDateTime(from!.Value);
            if (rule.Stage != PostingStage.Approval) rule.CategoryId = null;
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, _, error) = await Http.ExecuteRequestAsync<ApiResponse<PostingRuleDto>>(HttpVerb.Post, "api/accounting/posting-rules", rule);

            if (!ok)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to add the rule.", Severity.Error);
                return;
            }

            Snackbar.Add("Posting rule added.", Severity.Success);
            SideDialog.Close(true);
        }
        finally
        {
            busy = false;
        }
    }
}
