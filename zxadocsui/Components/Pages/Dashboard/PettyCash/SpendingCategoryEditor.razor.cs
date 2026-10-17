using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocsui.Srevices;

namespace zxadocsui.Components.Pages.Dashboard.PettyCash;

/// <summary>Adds or edits a spending category and its billing units in the side panel; closes with true when it saved.</summary>
public partial class SpendingCategoryEditor
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    [Parameter] public SpendingCategoryDto Category { get; set; } = new();
    [Parameter] public List<BillingUnitDto> Units { get; set; } = new();

    private IReadOnlyCollection<int> selectedUnits = [];
    private bool busy;

    protected override void OnInitialized() => selectedUnits = Category.BillingUnitIds;

    private async Task Save()
    {
        busy = true;
        try
        {
            Category.BillingUnitIds = selectedUnits.ToArray();
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, _, error) = Category.Id == 0
                ? await Http.ExecuteRequestAsync<ApiResponse<SpendingCategoryDto>>(HttpVerb.Post, "api/petty-cash/categories", Category)
                : await Http.ExecuteRequestAsync<ApiResponse<SpendingCategoryDto>>(HttpVerb.Put, $"api/petty-cash/categories/{Category.Id}", Category);

            if (!ok)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to save the category.", Severity.Error);
                return;
            }

            Snackbar.Add(Category.Id == 0 ? "Category added." : "Category updated.", Severity.Success);
            SideDialog.Close(true);
        }
        finally
        {
            busy = false;
        }
    }
}
