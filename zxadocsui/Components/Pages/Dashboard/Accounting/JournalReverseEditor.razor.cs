using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocsui.Srevices;

namespace zxadocsui.Components.Pages.Dashboard.Accounting;

/// <summary>Reverses a journal with a reason (ZD-235) in the side panel; closes with true when it posted.</summary>
public partial class JournalReverseEditor
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    [Parameter] public JournalDto Journal { get; set; } = new();

    private string reason = string.Empty;
    private bool busy;

    private async Task Save()
    {
        busy = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, result, error) = await Http.ExecuteRequestAsync<ApiResponse<JournalDto>>(
                HttpVerb.Post, $"api/accounting/journals/{Journal.Id}/reverse", new { Reason = reason });

            if (!ok)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to reverse the journal.", Severity.Error);
                return;
            }

            Snackbar.Add($"Posted {result?.Data?.Number}.", Severity.Success);
            SideDialog.Close(true);
        }
        finally
        {
            busy = false;
        }
    }
}
