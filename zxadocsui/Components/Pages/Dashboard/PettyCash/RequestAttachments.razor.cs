using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;

namespace zxadocsui.Components.Pages.Dashboard.PettyCash;

/// <summary>A request's attachments (ZD-217): editable by the maker on a draft, read-only elsewhere. The API checks type and size.</summary>
public partial class RequestAttachments
{
    // The API's configurable limit tops out at 4 MB; this only bounds what the browser will read.
    private const long MaxReadBytes = 4L * 1024 * 1024;

    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    [Parameter] public int RequestId { get; set; }
    [Parameter] public bool Editable { get; set; }

    private List<RequestAttachmentDto> attachments = new();
    private bool busy;

    protected override async Task OnParametersSetAsync()
    {
        if (RequestId == 0) return;
        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (ok, result, _) = await Http.GetAsync<ApiResponse<List<RequestAttachmentDto>>>($"api/petty-cash/requests/{RequestId}/attachments");
        attachments = ok && result?.Data is not null ? result.Data : new();
    }

    private async Task Upload(IBrowserFile? file)
    {
        if (file is null) return;
        if (file.Size > MaxReadBytes)
        {
            Snackbar.Add($"{file.Name} is larger than 4 MB.", Severity.Warning);
            return;
        }
        busy = true;
        try
        {
            using var stream = file.OpenReadStream(MaxReadBytes);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);

            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, added, error) = await Http.UploadFileAsync<ApiResponse<RequestAttachmentDto>>(
                $"api/petty-cash/requests/{RequestId}/attachments", buffer.ToArray(), file.Name, file.ContentType);
            if (!ok || added?.Data is null)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Could not attach the file.", Severity.Error);
                return;
            }
            attachments.Add(added.Data);
            Snackbar.Add($"{added.Data.FileName} attached.", Severity.Success);
        }
        catch (IOException ex)
        {
            Snackbar.Add("Could not read the file. " + ex.Message, Severity.Error);
        }
        finally
        {
            busy = false;
        }
    }

    private async Task Download(RequestAttachmentDto attachment)
    {
        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (ok, bytes, error) = await Http.GetBytesAsync($"api/petty-cash/attachments/{attachment.Id}");
        if (!ok || bytes is null)
        {
            Snackbar.Add(ErrorMessage.Extract(error) ?? "Could not download the file.", Severity.Error);
            return;
        }
        await JS.InvokeVoidAsync("zxFiles.save", attachment.FileName, Convert.ToBase64String(bytes), attachment.ContentType);
    }

    private async Task Remove(RequestAttachmentDto attachment)
    {
        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (ok, _, error) = await Http.ExecuteRequestAsync<ApiResponse<int>>(HttpVerb.Delete, $"api/petty-cash/attachments/{attachment.Id}");
        if (!ok)
        {
            Snackbar.Add(ErrorMessage.Extract(error) ?? "Could not remove the file.", Severity.Error);
            return;
        }
        attachments.Remove(attachment);
    }
}
