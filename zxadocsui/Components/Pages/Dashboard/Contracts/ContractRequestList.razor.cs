using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Contracts;

// Contract initiation requests (/contract-requests, ZD-120). Any contract permission may look;
// InitiateContract may change, which the server enforces independently.
public partial class ContractRequestList
{
    [Inject] private IContractRequestClientService RequestsApi { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private IDialogService Dialogs { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private List<ContractRequestDto> requests = new();
    private bool loading = true;
    private bool canInitiate;
    private bool canDraft;

    // After first render, not OnInitializedAsync: the token is not hydrated there, so the
    // permission fetch returns empty and the page bounces someone who holds the right.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        var perms = await Permissions.GetPermissions();
        canInitiate = perms.Contains(Permission.InitiateContract);
        canDraft = perms.Contains(Permission.CreateDraft);

        if (!perms.HasAnyContractPermission())
        {
            Snackbar.Add("You do not have permission to view contract requests.", Severity.Warning);
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
            var (ok, data, error) = await RequestsApi.List();
            if (!ok)
            {
                Snackbar.Add(error ?? "Failed to load contract requests.", Severity.Error);
                return;
            }
            requests = data.ToList();
        }
        finally
        {
            loading = false;
        }
    }

    private static Color StatusColour(ContractRequestStatus status) =>
        status == ContractRequestStatus.Drafted ? Color.Success : Color.Default;

    private async Task ConfirmDelete(ContractRequestDto request)
    {
        var confirmed = await Dialogs.ShowMessageBoxAsync(
            "Delete request",
            $"Delete {request.RequestReference}? Its details, KPIs and attached documents go with it. "
            + "This cannot be undone.",
            yesText: "Delete", cancelText: "Keep");

        if (confirmed != true) return;

        var (ok, error) = await RequestsApi.Delete(request.Id);
        if (!ok)
        {
            Snackbar.Add(error ?? "Failed to delete the request.", Severity.Error);
            return;
        }

        Snackbar.Add($"{request.RequestReference} deleted.", Severity.Success);
        await Load();
        StateHasChanged();
    }
}
