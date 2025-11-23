using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;
using zxadocsfe.Dtos;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;

namespace zxadocsapp.Components.Pages;

public partial class CreateDocument
{
    [Inject] ISnackbar? Snackbar { get; set; } = default;
    [Inject] ILogger<CreateDocument>? logger { set; get; }
    [Inject] private IHttpService httpSvc { get; set; } = default!;
    string fileBase64 = string.Empty, docType = string.Empty, userId = string.Empty, fileName = "File Name", docRef = string.Empty, organisationId = string.Empty;
    private double UploadProgress { get; set; }

    private List<ListOption> priorityList = new(), doctypeList = new(), docCatList = new();
    private List<DocAttachment> attachmentList = new();

    Document document = new();
    private byte[] fileBytes = default!;
    // bool success;
    // string[] errors = { };
    protected override void OnInitialized()
    {
        userId = "1";
        organisationId = "1";
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
                Snackbar?.Clear();
                Snackbar?.Add(message!, Severity.Normal);
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
            fileName = file.Name;
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
            fileBytes = ms.ToArray();
            fileBase64 = Convert.ToBase64String(fileBytes);
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


        }
        catch (Exception ex)
        {

        }
    }

    private void InitializeDocumentAttachements(List<DocAttachment> attachments)
    {
        attachmentList = attachments;
        Console.WriteLine($"InitializeDocumentAttachements <~><~><~><~><~> {attachments?.Count}");
    }
    private async Task SubmitDocument()
    {
        try
        {
            var (uploaded, uploadResult) = await UploadDocumentToServer();
            if (uploaded == false || string.IsNullOrEmpty(docRef))
            {
                // show toaster, message = "Document reference has not yet been generated"
                return;
            }
            document.AuthorId = int.Parse(userId);
            document.DocumentReference = docRef;
            document.Path = uploadResult;
            document.Amendments = attachmentList.Select(dd => new DocumentAmendment
            {
                Content = dd.Type == AppConstants.AttachmentType.Signature ? "" : dd.Content,
                Height = dd.Height,
                Width = dd.Width,
                PositionX = dd.PositionX,
                PositionY = dd.PositionY,
                Page = dd.Page - 1,
                Type = dd.Type,
                OrganisationId = int.Parse(organisationId),
            }).ToArray();
            var (status, result, message) = await httpSvc!.ExecuteRequestAsync<ApiResponse<string>>(HttpVerb.Post, $"api/document", document);
            if (status == false || result?.Data == null)
            {
                //show dialog at this point
                return;
            }

            Console.WriteLine("Document has successfully been uploaded to the remote sever");
        }
        catch (Exception ex)
        {
            logger!.LogDebug(ex.Message);
        }
    }

    private async Task<(bool, string)> UploadDocumentToServer()
    {
        try
        {
            if (fileBytes == null || fileBytes.Length <= 0)
            {
                // show toaster, message = "Document reference has not yet been generated"
                logger!.LogInformation("Couldn't proceed with the upload");
                return (false, "Couldn't proceed with the upload");
            }

            logger!.LogInformation("Proceeding to send to the server");
            var _docRef = Guid.NewGuid().ToString();
            var (status, result, message) = await httpSvc!.UploadDocumentAsync<DocUploadResult>($"api/upload", fileBytes, userId, _docRef, fileName, $"store_{document.TypeId}");
            if (status == false || result?.Name == null)
            {
                logger!.LogInformation(message);
                return (false, message!);
            }

            docRef = _docRef;
            return (true, result?.Name!);
        }
        catch (Exception ex)
        {
            logger!.LogDebug(ex.Message);
            return (false, ex.Message);
        }

    }

}
