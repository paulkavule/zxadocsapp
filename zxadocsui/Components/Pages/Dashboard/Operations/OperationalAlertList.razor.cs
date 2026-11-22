using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.Srevices;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Operations;

/// <summary>Operational alerts (ZD-176) for ManagePettyCashOperations holders to review and acknowledge.</summary>
public partial class OperationalAlertList
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    private List<OperationalAlertDto> items = new();
    private bool? open = true;
    private bool loading = true;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        if (!await Permissions.Has(Permission.ManagePettyCashOperations))
        {
            Snackbar.Add("You do not have access to operational alerts.", Severity.Warning);
            Nav.NavigateTo("/dashboard");
            return;
        }

        await Load();
        StateHasChanged();
    }

    private async Task Filter(bool? value)
    {
        open = value;
        await Load();
    }

    private async Task Load()
    {
        loading = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var url = open is null ? "api/operations/alerts" : $"api/operations/alerts?open={open.Value.ToString().ToLowerInvariant()}";
            var (ok, result, error) = await Http.GetAsync<ApiResponse<List<OperationalAlertDto>>>(url);
            if (!ok || result?.Data is null)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to load alerts.", Severity.Error);
                return;
            }
            items = result.Data;
        }
        finally
        {
            loading = false;
        }
    }

    private async Task Acknowledge(OperationalAlertDto alert)
    {
        var acknowledged = await SideDialog.Show<AlertAcknowledgeEditor, bool?>(
            new Dictionary<string, object> { { nameof(AlertAcknowledgeEditor.Alert), alert } }, title: "Acknowledge alert");
        if (acknowledged == true)
        {
            await Load();
            StateHasChanged();
        }
    }
}
