using System;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using zxadocsui.Components.Custom.Dialogs;
using zxadocsui.Dtos;
using zxadocsfe.Dtos;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using Microsoft.AspNetCore.Components.Forms;
using System.Runtime.CompilerServices;
using zxadocsui.State;

namespace zxadocsui.Components.DocWorkflow;

public partial class DocumentEditor
{
    [Inject] ISnackbar Snackbar { set; get; } = default!;
    [Inject] ILogger<DocumentEditor> logger { set; get; } = default!;
    [Inject] IDialogService dialog { set; get; } = default!;
    [Inject] IHttpService httpSvc { get; set; } = default!;
    [Inject] IUserSession session { get; set; } = default!;
    [Inject] IJSRuntime JS { get; set; } = default!;
    private readonly List<string> activeSignatures = new();
    [Parameter] public bool DisableEdits { get; set; } = true;
    [Parameter] public Document Document { get; set; } = new();
    [Parameter] public string UserId { get; set; } = string.Empty;
    [Parameter] public string DocId { get; set; } = string.Empty;
    [Parameter] public EventCallback<int> OnPageChanged { get; set; }

    private DotNetObjectReference<DocumentEditor>? _dotRef;
    private string _containerId = $"pdf_{Guid.NewGuid():N}", base64File;
    private string signatureUrl = string.Empty;
    // private List<ElementReference> references = new();
    private List<DocAttachment> attachments = new();

    private int divCount = 0;
    private double maxCanvasWidth = 300, maxCanvasHeight = 500, uploadProgress;

    public int CurrentPage { get; private set; } = 1;
    public int PageCount { get; private set; } = 0;
    public double Scale { get; private set; } = 1;
    private bool documentEdited = false;
    private bool hasInitialized = false, addSignature = false, addComment = false, shouldRender = false;
    private List<string> commentList = new List<string>();

    UserData userData = new();
    // [Parameter] public EventCallback<List<DocAttachment>> InitializeAttachments { set; get; }
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Console.WriteLine($" OnAfterRenderAsync ---------------> {string.IsNullOrEmpty(FileUrl)}");
        if (firstRender)
        {
            await LoadUserInformation();

            if (!string.IsNullOrEmpty(DocId))
                await LoadDocumentInformation();



            Console.WriteLine($"OnAfterRenderAsync =================> 1 ");
            _dotRef = DotNetObjectReference.Create(this);

        }

        if (hasInitialized == false)
        {
            try
            {
                if (string.IsNullOrEmpty(base64File))
                    return;
                var _ = Convert.FromBase64String(base64File);

                // Console.WriteLine($"OnAfterRenderAsync =================> 2 {FileUrl}");
                var meta = await JS.InvokeAsync<InitResult>("blazorPdf.init", _containerId, base64File, _dotRef);
                PageCount = meta.pageCount;
                Console.WriteLine("meta data ====> " + meta.pageCount + " - " + meta.scale + " - " + meta.width + " - " + meta.height);
                // Scale = meta.scale;
                CurrentPage = 1;
                StateHasChanged();
                hasInitialized = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading PDF module: {ex.Message}");
            }
        }
    }

    // protected override async Task OnInitializedAsync()
    // {
    //     if (!string.IsNullOrEmpty(UserId))
    //         await LoadUserInformation();

    //     if (!string.IsNullOrEmpty(DocId))
    //         await LoadDocumentInformation();
    // }

    private async Task LoadUserInformation()
    {
        userData = await session!.GetCurrentUser();
        if (userData == null)
        {
            Snackbar.Clear();
            Snackbar.Add("Couldn't retrieve user details. Signature not intialized", Severity.Error);
            return;

        }

        var (success, user, message) = await httpSvc.GetAsync<ApiResponse<User>>($"/api/users/{userData.UserId}");
        if (success == false)
            return;

        signatureUrl = user.Data.Signature ?? string.Empty;
    }
    private async Task LoadDocumentInformation()
    {
        try
        {
            string fileName = $"doc_{DocId}.pdf";

            var fullpath = Path.GetFullPath(fileName);
            logger!.LogInformation("********* Going to render full ************ " + fullpath);
            Console.WriteLine("********* Going to render full ************ " + fullpath);
            Snackbar?.Clear();
            Snackbar?.Add("Downloading file. Please wait....", Severity.Info);
            await using var fileStream = File.Create(fileName);

            await foreach (var chunk in httpSvc.DownloadDocumentFileAsync(docId: int.Parse(DocId)))
                await fileStream.WriteAsync(chunk, 0, chunk.Length);


            Snackbar?.Clear();
            Snackbar?.Add("Downloading complete. Please proceed....", Severity.Info);

            fileStream.Flush();
            fileStream.Close();

            base64File = Convert.ToBase64String(File.ReadAllBytes(fileName));

            StateHasChanged();
        }
        catch (Exception ex)
        {
            Snackbar?.Clear();
            Snackbar?.Add("Document download failed.", Severity.Error);
            logger.LogError(ex.Message);
        }
    }
    // protected override bool ShouldRender()
    // {
    //     try
    //     {
    //         if (shouldRender) return false;
    //         var _ = Convert.FromBase64String(FileUrl);
    //         shouldRender = true;
    //     }
    //     catch
    //     {
    //         shouldRender = false;
    //     }
    //     finally
    //     {
    //         Console.WriteLine($"ShouldRender called {shouldRender}");
    //     }
    //     return shouldRender;
    // }

    protected override async Task OnParametersSetAsync()
    {

        Console.WriteLine($"OnParametersSetAsync FileUrl changed");
        StateHasChanged();
        await Task.CompletedTask;
    }
    void Dragged((double X, double Y) pos) =>
    Console.WriteLine($"X={pos.X}, Y={pos.Y}");


    [JSInvokable]
    public async Task OnPdfPageChanged(int page, double scale, double width, double height)
    {
        Console.WriteLine($"OnPdfPageChanged =======> Page: {page}, Scale: {scale}, Width: {width}, Height: {height}");
        maxCanvasWidth = width;
        maxCanvasHeight = height;
        CurrentPage = page;
        Scale = scale;
        // var attachment = attachments?.Where(at => at.Page == CurrentPage).FirstOrDefault();
        // if (attachment != null)
        // {
        //     attachment.PageWidth = maxCanvasWidth;
        //     attachment.PageHeight = maxCanvasHeight;
        // }
        await OnPageChanged.InvokeAsync(page);
        // StateHasChanged();
    }

    private Task Next() => JS.InvokeVoidAsync("blazorPdf.nextPage").AsTask();
    private Task Prev() => JS.InvokeVoidAsync("blazorPdf.prevPage").AsTask();
    private Task ZoomIn() => JS.InvokeVoidAsync("blazorPdf.zoomIn").AsTask();
    private Task ZoomOut() => JS.InvokeVoidAsync("blazorPdf.zoomOut").AsTask();
    private record InitResult(int pageCount, double scale, double width, double height);
    private async Task GoTo(ChangeEventArgs e)
    {
        if (int.TryParse(Convert.ToString(e.Value), out var n))
        {
            await JS.InvokeVoidAsync("blazorPdf.goToPage", n);
        }
    }
    private async Task AddSign()
    {
        Console.WriteLine("AddSign ====> Clicked page: " + CurrentPage);
        addSignature = true;
        divCount++;
        var elementId = $"signature_{divCount}";
        attachments.Add(new DocAttachment
        {
            ElementId = elementId,
            Content = "user base64 signature",
            Type = AppConstants.AttachmentType.Signature,
            Page = CurrentPage,
            PositionX = 0,
            PositionY = 0,
            Width = 220,
            Height = 60
        });
        // StateHasChanged();
        await Task.Delay(100);

        await JS.InvokeVoidAsync("initializeDrag", _containerId, elementId, _dotRef);

    }

    private async Task AddComment()
    {
        var options = new DialogOptions { CloseOnEscapeKey = true };
        var dialogReference = await dialog.ShowAsync<TextInputDialog>("Adding Comment", options);
        var dialogResult = await dialogReference.Result;
        if (dialogResult!.Canceled || dialogResult.Data == null)
            return;
        string comment = (string)dialogResult.Data;
        if (string.IsNullOrEmpty(comment))
            return;

        commentList.Add(comment!);
        divCount++;
        var elementId = $"comment_{divCount}";
        attachments.Add(new DocAttachment
        {
            ElementId = elementId,
            Content = comment,
            Type = AppConstants.AttachmentType.Comment,
            Page = CurrentPage,
            PositionX = 0,
            PositionY = 0,
            Width = 220,
            Height = 100
        });
        addComment = true;
        await InvokeAsync(StateHasChanged);
        await Task.Delay(100);
        await JS.InvokeVoidAsync("initializeDrag", _containerId, elementId, _dotRef);

    }
    private async Task SaveDocument()
    {
        foreach (var att in attachments)
        {
            var attributes = await JS.InvokeAsync<TargetAttribute>(
                "getBoxRelativeToContainer",
                _containerId,
                att.ElementId
            );
            Console.WriteLine($"Attributes for {att.ElementId} : {attributes.PositionX}, {attributes.PositionY}, {attributes.Width}, {attributes.Height}");
            // att.PositionX = (int)attributes.PositionX;
        }
    }
    private async Task EditComment(DocAttachment attachment)
    {
        Console.WriteLine("EditComment ====> Clicked");
        var parameters = new DialogParameters
        {
            [nameof(TextInputDialog.InitialText)] = attachment.Content
        };
        var options = new DialogOptions { CloseOnEscapeKey = true };
        var dialogRef = await dialog.ShowAsync<TextInputDialog>(
            "Edit text dialog",          // Dialog header (can be different from Title param)
            parameters,
            options
        );

        var result = await dialogRef.Result;

        if (result!.Canceled || result.Data == null)
            return;

        string comment = (string)result.Data;
        if (string.IsNullOrEmpty(comment))
            return;

        attachment.Content = comment;
    }
    private async Task DeleteItem(DocAttachment attachment)
    {
        attachments.Remove(attachment);
        Console.WriteLine($"This item should be deleted now now now {attachment.ElementId}");
    }

    public async ValueTask DisposeAsync()
    {
        try { await JS.InvokeVoidAsync("blazorPdf.dispose"); } catch { }

        _dotRef?.Dispose();
    }

    private async Task InitializeDrag(string elementId)
    {
        Console.WriteLine($"InitializeDrag ====> {elementId}");
        if (activeSignatures.Contains(elementId)) return;
        activeSignatures.Add(elementId);
        var attachment = attachments.Where(at => at.ElementId == elementId).FirstOrDefault();
        if (attachment == null || attachment?.StopDragClick == true) return;

        Console.WriteLine($"Initializing drag for {elementId}");
        attachment.StopDragClick = true;
        await JS.InvokeVoidAsync("initializeDrag", _containerId, elementId, _dotRef);

    }


    [JSInvokable]
    public async Task OnDragEnd(string elementId, double x, double y, double w, double h, double pw, double ph)
    {
        Console.WriteLine($"OnDragEnd x: {x} - y: {y} || w: {w} - h:{h}|| {pw} for {ph}");
        var attachment = attachments?.Where(at => at.ElementId == elementId).FirstOrDefault();
        if (attachment == null) return;
        attachment.PositionX = (int)x;
        attachment.PositionY = (int)y;
        attachment.Width = (int)w;
        attachment.Height = (int)h;
        attachment.PageWidth = pw;
        attachment.PageHeight = ph;

        await InvokeAsync(StateHasChanged);
        // InitializeAttachments.InvokeAsync(attachments);
    }

    // [JSInvokable]
    // public Task OnResize(double x, double y, double w, double h)
    // => OnResizeInit.HasDelegate ? OnResizeInit.InvokeAsync(new BoxRect(x, y, w, h)) : Task.CompletedTask;

    [JSInvokable]
    public async Task OnResizeEnd(string elementId, double x, double y, double w, double h, double pw, double ph)
    {
        Console.WriteLine($"OnResize Ended ======>>>>>>>>> {elementId} : {x}, {y}, {w}, {h}");
        var attachment = attachments.Where(at => at.ElementId == elementId).FirstOrDefault();
        if (attachment == null) return;
        attachment.PositionX = (int)x;
        attachment.PositionY = (int)y;
        attachment.Width = (int)w;
        attachment.Height = (int)h;
        attachment.PageWidth = pw;
        attachment.PageHeight = ph;
        await InvokeAsync(StateHasChanged);
        // InitializeAttachments.InvokeAsync(attachments);
    }

    public List<DocAttachment> GetAttchments()
    {
        return attachments;
    }

    public bool GetDocumentEditStatus()
    {
        return documentEdited;
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
                uploadProgress = percentage;
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
            // await JS.InvokeVoidAsync("resetCanvas", _containerId);

            var fileBytes = ms.ToArray();
            base64File = Convert.ToBase64String(fileBytes);
            attachments.Add(new DocAttachment { Content = base64File, Type = AppConstants.AttachmentType.Document });
            // await JS.InvokeAsync<InitResult>("blazorPdf.init", _containerId, base64File, _dotRef);
            Console.WriteLine($"string length from uploadfiledocument is {base64File.Length}");
            StateHasChanged();
        }
        catch (Exception ee)
        {
            logger!.LogDebug(ee.StackTrace);
        }
    }


    // => OnResizeEndInit.HasDelegate ? OnResizeEndInit.InvokeAsync(new BoxRect(x, y, w, h)) : Task.CompletedTask;


    // public record struct BoxRect(double X, double Y, double Width, double Height);

}
