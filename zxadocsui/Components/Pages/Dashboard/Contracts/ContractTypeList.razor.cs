using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Contracts;

// The contract type catalogue (/contract-types, ZD-115). Any contract permission may look;
// only CreateContractTypes may change anything, which the server enforces independently.
public partial class ContractTypeList
{
    [Inject] private IContractTypeClientService TypesApi { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private IDialogService Dialogs { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private List<ContractTypeDto> types = new();
    private bool loading = true;
    private bool canManage;

    // After first render, not OnInitializedAsync: the token is not hydrated there, so the
    // permission fetch returns empty and the page bounces someone who holds the right.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        var perms = await Permissions.GetPermissions();
        canManage = perms.Contains(Permission.CreateContractTypes);

        if (!perms.HasAnyContractPermission())
        {
            Snackbar.Add("You do not have permission to view contract types.", Severity.Warning);
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
            var (ok, data, error) = await TypesApi.List();
            if (!ok)
            {
                Snackbar.Add(error ?? "Failed to load contract types.", Severity.Error);
                return;
            }
            types = data.ToList();
        }
        finally
        {
            loading = false;
        }
    }

    private async Task ConfirmDelete(ContractTypeDto type)
    {
        var confirmed = await Dialogs.ShowMessageBoxAsync(
            "Delete contract type",
            $"Delete \"{type.Name}\"? It stops being offered for new contracts. "
            + "Contracts already drafted against it keep their fields.",
            yesText: "Delete", cancelText: "Cancel");

        if (confirmed != true) return;

        var (ok, error) = await TypesApi.Delete(type.Id);
        if (!ok)
        {
            Snackbar.Add(error ?? "Failed to delete the contract type.", Severity.Error);
            return;
        }

        Snackbar.Add($"Deleted {type.Name}.", Severity.Success);
        await Load();
        StateHasChanged();
    }
}

public static class ContractPermissionExtensions
{
    private static readonly Permission[] Any =
    {
        Permission.ViewContracts, Permission.InitiateContract, Permission.ReviewContract,
        Permission.ApproveContract, Permission.CreateContractTypes,
    };

    // Mirrors PermissionGate.ContractReaders server-side.
    public static bool HasAnyContractPermission(this IReadOnlySet<Permission> held) =>
        Any.Any(held.Contains);
}
