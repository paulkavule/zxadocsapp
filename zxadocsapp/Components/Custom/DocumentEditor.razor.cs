using System;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using zxadocsapp.Components.Custom.Dialogs;
using zxadocsfe.Dtos;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;

namespace zxadocsapp.Components.Custom;

public partial class DocumentEditor
{
    [Inject] IDialogService dialogSvc { set; get; }
    [Inject] IHttpService httpSvc { get; set; }
    [Inject] IJSRuntime JS { get; set; } = default!;
    private readonly List<RenderFragment> fragments = new();
    private readonly List<string> activeSignatures = new();

    //[Parameter] public int Width { get; set; } = 100;
    //[Parameter] public int Height { get; set; } = 100;
    [Parameter] public string UserId { get; set; } = string.Empty;
    [Parameter] public string DocId { get; set; } = string.Empty;
    [Parameter] public string FileUrl { get; set; } = string.Empty;
    [Parameter] public EventCallback<int> OnPageChanged { get; set; }

    private DotNetObjectReference<DocumentEditor>? _dotRef;
    private string _containerId = $"pdf_{Guid.NewGuid():N}";
    private ElementReference pdfCanvas;
    private ElementReference stageRef;
    private ElementReference dragRef;

    // private List<ElementReference> references = new();
    private List<DocAttachment> attachments = new();

    private IJSObjectReference? _module;
    private IJSObjectReference? _dragHandle;
    private double _upX, _upY, width, height;
    private int divCount = 0;
    public int CurrentPage { get; private set; } = 1;
    public int PageCount { get; private set; } = 0;
    private bool hasInitialized = false, addSignature, addComment, shouldRender;
    private List<string> commentList = new List<string>();
    [Parameter] public EventCallback<List<DocAttachment>> InitializeAttachments { set; get; }
    // [Parameter] public EventCallback<BoxRect> OnResizeInit { get; set; }
    // [Parameter] public EventCallback<BoxRect> OnResizeEndInit { get; set; }


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Console.WriteLine($" OnAfterRenderAsync ---------------> {string.IsNullOrEmpty(FileUrl)}");
        if (firstRender)
        {
            Console.WriteLine($"OnAfterRenderAsync =================> 1 ");
            _dotRef = DotNetObjectReference.Create(this);
        }

        if (hasInitialized == false)
        {
            try
            {
                if (string.IsNullOrEmpty(FileUrl))
                    return;
                var _ = Convert.FromBase64String(FileUrl);
                Console.WriteLine($"OnAfterRenderAsync =================> 2 ");

                // Console.WriteLine($"OnAfterRenderAsync =================> 2 {FileUrl}");
                var meta = await JS.InvokeAsync<InitResult>("blazorPdf.init", _containerId, FileUrl, _dotRef);
                PageCount = meta.pageCount;
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

    protected override async Task OnInitializedAsync()
    {
        if (!string.IsNullOrEmpty(UserId))
            await LoadUserInformation();

        if (!string.IsNullOrEmpty(DocId))
            await LoadDocumentInformation();
    }

    private async Task LoadUserInformation()
    {
        var user = await httpSvc.GetAsync<ApiResponse<List<User>>>($"/api/users/{UserId}");
    }
    private async Task LoadDocumentInformation()
    {
        var documents = await httpSvc.GetAsync<ApiResponse<List<User>>>($"/api/documents/{DocId}");
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
    public async Task OnPdfPageChanged(int page)
    {
        CurrentPage = page;
        await OnPageChanged.InvokeAsync(page);
        StateHasChanged();
    }

    private Task Next() => JS.InvokeVoidAsync("blazorPdf.nextPage").AsTask();
    private Task Prev() => JS.InvokeVoidAsync("blazorPdf.prevPage").AsTask();
    private Task ZoomIn() => JS.InvokeVoidAsync("blazorPdf.zoomIn").AsTask();
    private Task ZoomOut() => JS.InvokeVoidAsync("blazorPdf.zoomOut").AsTask();
    private record InitResult(int pageCount);
    private async Task GoTo(ChangeEventArgs e)
    {
        if (int.TryParse(Convert.ToString(e.Value), out var n))
        {
            await JS.InvokeVoidAsync("blazorPdf.goToPage", n);
        }
    }
    private async Task AddSign()
    {
        attachments.Add(new DocAttachment
        {
            ElementId = $"comment{attachments.Count}",
            Content = "user base64 signature",
            Type = AppConstants.AttachmentType.Signature
        });
    }

    private async Task AddComment()
    {
        var options = new DialogOptions { CloseOnEscapeKey = true };
        var dialogReference = await dialogSvc.ShowAsync<TextInputDialog>("Adding Comment", options);
        var dialogResult = await dialogReference.Result;
        if (dialogResult!.Canceled || dialogResult.Data == null)
            return;
        string comment = (string)dialogResult.Data;
        if (string.IsNullOrEmpty(comment))
            return;

        commentList.Add(comment!);
        attachments.Add(new DocAttachment
        {
            ElementId = $"comment{attachments.Count}",
            Content = comment,
            Type = AppConstants.AttachmentType.Comment
        });
        addComment = true;
        divCount++;
        // await JS.InvokeVoidAsync("initializeDrag", _containerId, "userComment", _dotRef);
    }

    private async Task EditComment(DocAttachment attachment)
    {
        Console.WriteLine("EditComment ====> Clicked");
        var parameters = new DialogParameters
        {
            [nameof(TextInputDialog.InitialText)] = attachment.Content
        };
        var options = new DialogOptions { CloseOnEscapeKey = true };
        var dialogRef = await dialogSvc.ShowAsync<TextInputDialog>(
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
        Console.WriteLine($"Initializing drag for {elementId}");
        await JS.InvokeVoidAsync("initializeDrag", _containerId, elementId, _dotRef);
    }


    [JSInvokable]
    public void OnDragEnd(string elementId, double x, double y)
    {
        Console.WriteLine($"OnDragEnd {x} - {y}");
        var attachment = attachments.Where(at => at.ElementId == elementId).FirstOrDefault();
        if (attachment == null) return;
        attachment.PositionX = (int)x;
        attachment.PositionY = (int)y;

        InitializeAttachments.InvokeAsync(attachments);
    }

    // [JSInvokable]
    // public Task OnResize(double x, double y, double w, double h)
    // => OnResizeInit.HasDelegate ? OnResizeInit.InvokeAsync(new BoxRect(x, y, w, h)) : Task.CompletedTask;

    [JSInvokable]
    public void OnResizeEnd(string elementId, double x, double y, double w, double h)
    {
        Console.WriteLine($"OnResize Ended ======>>>>>>>>> {elementId} : {x}, {y}, {w}, {h}");
        var attachment = attachments.Where(at => at.ElementId == elementId).FirstOrDefault();
        if (attachment == null) return;
        attachment.PositionX = (int)x;
        attachment.PositionY = (int)y;
        attachment.Width = (int)w;
        attachment.Height = (int)h;

        InitializeAttachments.InvokeAsync(attachments);
    }
    // => OnResizeEndInit.HasDelegate ? OnResizeEndInit.InvokeAsync(new BoxRect(x, y, w, h)) : Task.CompletedTask;


    // public record struct BoxRect(double X, double Y, double Width, double Height);

}
