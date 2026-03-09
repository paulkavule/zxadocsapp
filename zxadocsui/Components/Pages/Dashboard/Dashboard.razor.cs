using System;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace zxadocsui.Components.Pages.Dashboard;

public partial class Dashboard
{
    [Inject] IJSRuntime JSRuntime { set; get; } = default;
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        Console.WriteLine("-- -- -- -- -- -- >1");
        if (firstRender)
        {
            Console.WriteLine("Intializing the menu");
            await JSRuntime.InvokeVoidAsync("initializeSidebar");
        }

    }

    void Clicked()
    {
        Console.WriteLine("This is okay");
    }
}
