using System;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using zxadocsfe.Services;
using zxadocslib.Dtos;

namespace zxadocsapp.Components.Custom;

public partial class DocumentEditor
{
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

    private IJSObjectReference? _module;
    private IJSObjectReference? _dragHandle;
    private double _upX, _upY;
    private int divCount = 0;
    public int CurrentPage { get; private set; } = 1;
    public int PageCount { get; private set; } = 0;
    private bool hasInitialized = false;
    [Parameter] public EventCallback<BoxRect> OnResizeInit { get; set; }
    [Parameter] public EventCallback<BoxRect> OnResizeEndInit { get; set; }


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        Console.WriteLine($" OnAfterRenderAsync ---------------> {string.IsNullOrEmpty(FileUrl)}");
        if (firstRender)
        {

        }

        if (hasInitialized == false)
        {
            try
            {
                if (string.IsNullOrEmpty(FileUrl))
                    return;
                var _ = Convert.FromBase64String(FileUrl);
                Console.WriteLine($"OnAfterRenderAsync =================> 1 ");
                _dotRef = DotNetObjectReference.Create(this);
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
    protected override bool ShouldRender()
    {
        Console.WriteLine($"---------------> {string.IsNullOrEmpty(FileUrl)}");
        bool shouldRender = false;
        try
        {

            var _ = Convert.FromBase64String(FileUrl);
            shouldRender = true;
        }
        catch
        {
            shouldRender = false;
        }
        finally
        {
            Console.WriteLine($"ShouldRender called {shouldRender}");
        }
        return shouldRender;
    }

    protected override async Task OnParametersSetAsync()
    {

        Console.WriteLine($"OnParametersSetAsync FileUrl changed");
        StateHasChanged();
        await Task.CompletedTask;
        // try
        // {
        //     var _ = Convert.FromBase64String(FileUrl);
        //     _dotRef = DotNetObjectReference.Create(this);

        //     var meta = await JS.InvokeAsync<InitResult>("blazorPdf.init", _containerId, FileUrl, _dotRef);
        //     PageCount = meta.pageCount;
        //     CurrentPage = 1;
        //     StateHasChanged();
        // }
        // catch (Exception ex)
        // {
        //     Console.WriteLine($"Error loading PDF module: {ex.Message}");
        // }
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

    private void AddSign()
    {
        divCount++;
    }

    private async Task GoTo(ChangeEventArgs e)
    {
        if (int.TryParse(Convert.ToString(e.Value), out var n))
        {
            await JS.InvokeVoidAsync("blazorPdf.goToPage", n);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await JS.InvokeVoidAsync("blazorPdf.dispose"); } catch { }

        _dotRef?.Dispose();
    }

    private async Task InitializeDrag(string elementId)
    {
        if (activeSignatures.Contains(elementId)) return;
        activeSignatures.Add(elementId);
        Console.WriteLine($"Initializing drag for {elementId}");
        await JS.InvokeVoidAsync("initializeDrag", _containerId, elementId, _dotRef);
    }

    [JSInvokable]
    public void OnDragEnd(double x, double y)
    {
        _upX = x; _upY = y;
        // persist/save/snapping logic could go here
        InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public Task OnResize(double x, double y, double w, double h)
    => OnResizeInit.HasDelegate ? OnResizeInit.InvokeAsync(new BoxRect(x, y, w, h)) : Task.CompletedTask;

    [JSInvokable]
    public Task OnResizeEnd(double x, double y, double w, double h)
    => OnResizeEndInit.HasDelegate ? OnResizeEndInit.InvokeAsync(new BoxRect(x, y, w, h)) : Task.CompletedTask;


    public record struct BoxRect(double X, double Y, double Width, double Height);
    private record InitResult(int pageCount);
}
