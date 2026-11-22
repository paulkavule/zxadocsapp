using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using zxadocsfe.Helpers;
using zxadocsfe.Services;
using zxadocslib.Dtos;
using zxadocsui.State;

namespace zxadocsui.Components.Pages.Dashboard.Exports;

/// <summary>The signed-in user's queued exports (ZD-230), with a download once each is ready.</summary>
public partial class MyExports
{
    [Inject] private IHttpService Http { get; set; } = default!;
    [Inject] private IUserSession Session { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private List<ExportJobDto> jobs = new();
    private bool loading = true;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        await Session.GetCurrentUser();
        await Load();
        StateHasChanged();
    }

    private async Task Load()
    {
        loading = true;
        try
        {
            Http.Initialize(AppConstants.HttpSchemes.Core);
            var (ok, result, error) = await Http.GetAsync<ApiResponse<List<ExportJobDto>>>("api/exports");
            if (!ok || result?.Data is null)
            {
                Snackbar.Add(ErrorMessage.Extract(error) ?? "Failed to load your exports.", Severity.Error);
                return;
            }
            jobs = result.Data;
        }
        finally
        {
            loading = false;
        }
    }

    private async Task Download(ExportJobDto job)
    {
        Http.Initialize(AppConstants.HttpSchemes.Core);
        var (ok, _, contentType, bytes, error) = await Http.GetFileAsync($"api/exports/{job.Id}/file");
        if (!ok || bytes is null)
        {
            Snackbar.Add(ErrorMessage.Extract(error) ?? "Could not download the export.", Severity.Error);
            return;
        }
        await JS.InvokeVoidAsync("zxFiles.save", job.FileName, Convert.ToBase64String(bytes), contentType);
    }
}
