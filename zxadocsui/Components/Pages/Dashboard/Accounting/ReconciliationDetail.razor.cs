using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocslib.Helpers;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Accounting;

/// <summary>
/// One reconciliation exception with its request, journals, wallet movements and provider record (ZD-303),
/// and its resolution, or the form that records one (ZD-322). The API enforces every rule the form hints at.
/// </summary>
public partial class ReconciliationDetail
{
    [Parameter] public int Id { get; set; }

    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IPermissionClientService Permissions { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private const long MaxReadBytes = 4L * 1024 * 1024;

    private ReconciliationExceptionDetailDto? detail;
    private bool loading = true;
    private bool canResolve;
    private bool busy;
    private ReconciliationResolutionType? resolutionType;
    private int? correctionJournalId;
    private string comment = string.Empty;
    private (byte[] Content, string Name, string ContentType)? evidence;

    private IEnumerable<JournalDto> CorrectingJournals =>
        detail!.Journals.Where(j => j.PostedAt >= detail.Exception.FirstSeenAt);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        await Session.GetCurrentUser();
        if (!await Permissions.HasAny(Permission.ManageAccounting, Permission.ViewPettyCashFinance))
        {
            Snackbar.Add("You do not have access to reconciliation.", Severity.Warning);
            Nav.NavigateTo("/dashboard");
            return;
        }

        canResolve = await Permissions.Has(Permission.ManageAccounting);
        await Load();
    }

    private async Task Load()
    {
        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (ok, result, error) = await Http.GetAsync<ApiResponse<ReconciliationExceptionDetailDto>>($"api/accounting/reconciliation/exceptions/{Id}");
        if (ok)
            detail = result?.Data;
        else
            Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to load the exception.", Severity.Error);
        loading = false;
        StateHasChanged();
    }

    private async Task PickEvidence(IBrowserFile? file)
    {
        if (file is null) return;
        if (file.Size > MaxReadBytes)
        {
            Snackbar.Add($"{file.Name} is larger than 4 MB.", Severity.Warning);
            return;
        }
        using var stream = file.OpenReadStream(MaxReadBytes);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        evidence = (buffer.ToArray(), file.Name, file.ContentType);
    }

    private async Task Resolve()
    {
        if (resolutionType is null || string.IsNullOrWhiteSpace(comment))
        {
            Snackbar.Add("Choose a resolution type and give a comment.", Severity.Warning);
            return;
        }

        var fields = new Dictionary<string, string> { ["type"] = ((int)resolutionType).ToString(), ["comment"] = comment };
        if (resolutionType == ReconciliationResolutionType.Corrected && correctionJournalId is { } journalId)
            fields["correctionJournalId"] = journalId.ToString();

        busy = true;
        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (ok, _, error) = await Http.PostFormAsync<ApiResponse<ReconciliationExceptionDto>>(
            $"api/accounting/reconciliation/exceptions/{Id}/resolve", fields, evidence);
        busy = false;
        if (!ok)
        {
            Snackbar.Add(ErrorMessage.Extract(error) ?? "Could not resolve the exception.", Severity.Error);
            return;
        }

        Snackbar.Add("Exception resolved.", Severity.Success);
        await Load();
    }

    private async Task DownloadEvidence()
    {
        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (ok, bytes, error) = await Http.GetBytesAsync($"api/accounting/reconciliation/exceptions/{Id}/evidence");
        if (!ok || bytes is null)
        {
            Snackbar.Add(ErrorMessage.Extract(error) ?? "Could not download the evidence.", Severity.Error);
            return;
        }
        await JS.InvokeVoidAsync("zxFiles.save", detail!.Exception.EvidenceFileName, Convert.ToBase64String(bytes), "application/octet-stream");
    }
}
