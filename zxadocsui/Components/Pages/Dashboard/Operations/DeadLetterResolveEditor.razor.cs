using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocsui.Srevices;

namespace zxadocsui.Components.Pages.Dashboard.Operations;

/// <summary>Closes a dead letter without replaying it, with a note (ZD-139); closes with true when it saved.</summary>
public partial class DeadLetterResolveEditor
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    [Parameter] public DeadLetterItemDto Item { get; set; } = new();

    private string note = string.Empty;
    private bool busy;

    private async Task Save()
    {
        busy = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, _, error) = await Http.ExecuteRequestAsync<ApiResponse<DeadLetterItemDto>>(
                HttpVerb.Post, $"api/operations/dead-letters/{Item.Id}/resolve", new { Note = note });
            if (!ok)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to resolve.", Severity.Error);
                return;
            }
            Snackbar.Add("Resolved.", Severity.Success);
            SideDialog.Close(true);
        }
        finally
        {
            busy = false;
        }
    }
}
