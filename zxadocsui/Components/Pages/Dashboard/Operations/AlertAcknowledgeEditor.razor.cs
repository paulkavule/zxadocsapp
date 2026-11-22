using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocsui.Srevices;

namespace zxadocsui.Components.Pages.Dashboard.Operations;

/// <summary>Acknowledges an open alert with an optional note (ZD-176); closes with true when it saved.</summary>
public partial class AlertAcknowledgeEditor
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    [Parameter] public OperationalAlertDto Alert { get; set; } = new();

    private string note = string.Empty;
    private bool busy;

    private async Task Save()
    {
        busy = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, _, error) = await Http.ExecuteRequestAsync<ApiResponse<OperationalAlertDto>>(
                HttpVerb.Post, $"api/operations/alerts/{Alert.Id}/acknowledge", new { Note = note });
            if (!ok)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to acknowledge.", Severity.Error);
                return;
            }
            Snackbar.Add("Acknowledged.", Severity.Success);
            SideDialog.Close(true);
        }
        finally
        {
            busy = false;
        }
    }
}
