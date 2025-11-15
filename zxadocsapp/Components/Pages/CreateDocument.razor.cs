using System;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using zxadocsfe.Services;
using zxadocslib.Dtos;

namespace zxadocsapp.Components.Pages;

public partial class CreateDocument
{
    [Inject] private IHttpService httpService { get; set; } = default!;
    string fileBase64 = string.Empty;
    private double UploadProgress { get; set; }


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
