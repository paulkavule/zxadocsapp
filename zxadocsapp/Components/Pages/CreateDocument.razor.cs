using System;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;

namespace zxadocsapp.Components.Pages;

public partial class CreateDocument
{
    [Inject] ILogger<CreateDocument>? logger { set; get; }
    [Inject] private IHttpService httpSvc { get; set; } = default!;
    string fileBase64 = string.Empty, docType = string.Empty;
    private double UploadProgress { get; set; }

    private List<ListOption> priorityList = new(), doctypeList = new(), docCatList = new();

    Document document = new();
    // bool success;
    // string[] errors = { };
    protected override void OnInitialized()
    {
        httpSvc!.Initialize(AppConstants.HttpSchemes.Core);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await loadPriorities();
            await loadDocumentTypes();
        }
    }

    private async Task loadPriorities()
    {
        try
        {
            var (status, result, message) = await httpSvc!.GetAsync<ApiResponse<List<ListOption>>>("api/listoptions/1?type=Priority");
            if (status == false || result?.Data == null)
            {
                //show dialog at this point
                return;
            }

            priorityList = result.Data;
        }
        catch (Exception ex)
        {
            logger!.LogDebug(ex.Message);
        }
    }

    private async Task loadDocumentTypes()
    {
        try
        {
            var (status, result, message) = await httpSvc!.GetAsync<ApiResponse<List<ListOption>>>("api/listoptions/1?type=documenttype");
            if (status == false || result?.Data == null)
            {
                //show dialog at this point
                return;
            }

            doctypeList = result.Data;
        }
        catch (Exception ex)
        {
            logger!.LogDebug(ex.Message);
        }
    }

    private async Task DocTypeChanged(string value)
    {
        try
        {
            var (status, result, message) = await httpSvc!.GetAsync<ApiResponse<List<ListOption>>>($"api/listoptions/1?type=documentcategory&category={value}");
            if (status == false || result?.Data == null)
            {
                //show dialog at this point
                return;
            }

            docCatList = result.Data;
        }
        catch (Exception ex)
        {
            logger!.LogDebug(ex.Message);
        }
    }
    private async Task UploadFileDocument(IBrowserFile file)
    {
        try
        {


            var buffer = new byte[4096];
            long totalBytes = file.Size;
            long bytesRead = 0;
            Console.WriteLine($"Starting upload of file: {file.Name}, Size: {totalBytes} bytes");
            // Create a new progress object to report upload progress
            var progress = new Progress<double>(percentage =>
            {
                // This will be called as the upload progresses
                UploadProgress = percentage;
                StateHasChanged();
            });

            Console.WriteLine($"File size: {file.Size}\n");

            var stream = file.OpenReadStream(maxAllowedSize: 10485760);
            using var ms = new MemoryStream();
            int counter = 0;
            while (await stream.ReadAsync(buffer) is int read && read > 0)
            {
                await ms.WriteAsync(buffer.AsMemory(0, read));
                bytesRead += read;
                Console.WriteLine($"Reading bytes {counter}");
                // Calculate and report progress
                var percentage = (double)bytesRead / totalBytes * 100;

                // Console.WriteLine($"Upload progress: {percentage}%");
                ((IProgress<double>)progress).Report(percentage);
                await Task.Delay(20);
                counter++;
            }
            ms.ToArray();
            fileBase64 = Convert.ToBase64String(ms.ToArray());
            Console.WriteLine($"string length from uploadfiledocument is {fileBase64.Length}");
            StateHasChanged();
        }
        catch (Exception ee)
        {
            logger!.LogDebug(ee.StackTrace);
        }
    }
    private async Task UploadDocument(InputFileChangeEventArgs e)
    {
        var file = e.File;
        var buffer = new byte[4096];
        long totalBytes = file.Size;
        long bytesRead = 0;
        Console.WriteLine($"Starting upload of file: {file.Name}, Size: {totalBytes} bytes");
        // Create a new progress object to report upload progress
        var progress = new Progress<double>(percentage =>
        {
            // This will be called as the upload progresses
            UploadProgress = percentage;
            StateHasChanged();
        });

        try
        {
            // Create a new memory stream to store the file
            using var stream = file.OpenReadStream(maxAllowedSize: 10485760); // 10MB max
            using var ms = new MemoryStream();

            while (await stream.ReadAsync(buffer) is int read && read > 0)
            {
                await ms.WriteAsync(buffer.AsMemory(0, read));
                bytesRead += read;

                // Calculate and report progress
                var percentage = (double)bytesRead / totalBytes * 100;

                // Console.WriteLine($"Upload progress: {percentage}%");
                ((IProgress<double>)progress).Report(percentage);
                await Task.Delay(10);
            }
            ms.ToArray();
            fileBase64 = Convert.ToBase64String(ms.ToArray());
            Console.WriteLine($"string length from uploaddocument is {fileBase64.Length}");
            // Create form data
            // var content = new MultipartFormDataContent();
            // var fileContent = new ByteArrayContent(ms.ToArray());
            // fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            // content.Add(fileContent, "file", file.Name);
            // content.Add(new StringContent("1"), "userId");
            // content.Add(new StringContent(new Guid().ToString()), "documentReference");
            // content.Add(new StringContent("General"), "folder");


            // // Upload the file using your HttpService
            // var (success, response, error) = await httpService.ExecuteRequestAsync<ApiResponse<string>>(
            //     HttpVerb.Post,
            //     "api/upload",
            //     content
            // );


        }
        catch (Exception ex)
        {

        }
    }

}
