using Microsoft.AspNetCore.Components;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.Srevices;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Operations;

/// <summary>Dead-lettered messages (ZD-139) for ManagePettyCashOperations holders to replay or resolve.</summary>
public partial class DeadLetterList
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IDialogService Dialogs { get; set; } = default!;
    [Inject] private SideDialogService SideDialog { get; set; } = default!;

    private List<DeadLetterItemDto> items = new();
    private DeadLetterStatus? status = DeadLetterStatus.Open;
    private bool loading = true;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        if (!await Permissions.Has(Permission.ManagePettyCashOperations))
        {
            Snackbar.Add("You do not have access to dead letters.", Severity.Warning);
            Nav.NavigateTo("/dashboard");
            return;
        }

        await Load();
        StateHasChanged();
    }

    private async Task Filter(DeadLetterStatus? value)
    {
        status = value;
        await Load();
    }

    private async Task Load()
    {
        loading = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var url = status is null ? "api/operations/dead-letters" : $"api/operations/dead-letters?status={status}";
            var (ok, result, error) = await Http.GetAsync<ApiResponse<List<DeadLetterItemDto>>>(url);
            if (!ok || result?.Data is null)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to load dead letters.", Severity.Error);
                return;
            }
            items = result.Data;
        }
        finally
        {
            loading = false;
        }
    }

    private async Task Replay(DeadLetterItemDto item)
    {
        var confirmed = await Dialogs.ShowMessageBoxAsync("Replay message",
            $"Send {item.Type} for {item.TransactionId} back to {item.Consumer}?", yesText: "Replay", cancelText: "Cancel");
        if (confirmed != true) return;

        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (ok, _, error) = await Http.ExecuteRequestAsync<ApiResponse<DeadLetterItemDto>>(
            HttpVerb.Post, $"api/operations/dead-letters/{item.Id}/replay", new { });
        Snackbar.Add(ok ? "Queued for replay." : ErrorMessage.Extract(error) ?? "Replay failed.", ok ? Severity.Success : Severity.Error);
        await Load();
        StateHasChanged();
    }

    private async Task Resolve(DeadLetterItemDto item)
    {
        var resolved = await SideDialog.Show<DeadLetterResolveEditor, bool?>(
            new Dictionary<string, object> { { nameof(DeadLetterResolveEditor.Item), item } }, title: "Resolve dead letter");
        if (resolved == true)
        {
            await Load();
            StateHasChanged();
        }
    }
}
