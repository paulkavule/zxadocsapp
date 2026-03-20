using System;
using Microsoft.AspNetCore.Components;

namespace zxadocsui.Components.Pages.Dashboard;

public partial class Documents
{
    [Inject] NavigationManager Navigator { set; get; } = default!;

    private async Task CreateDocument()
    {
        Navigator.NavigateTo("/newdocument");
    }

}
