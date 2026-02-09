using System;
using BootstrapBlazor.Components;

namespace zxadocsuiapp.Components.Layout;

public partial class PlainLayout
{
    private IEnumerable<MenuItem> menuItems { get; set; } = new List<MenuItem>
    {
        new MenuItem()
        {
            Text = "Documents",
            Icon = "fa fa-database",
            Url = "/documents",
            Items = new List<MenuItem>
            {
                new MenuItem()
                {
                    Text = "Received",
                    Icon = "fa fa-envelope-open",
                    Url = "/documents/received"
                },
                new MenuItem()
                {
                    Text = "Sent",
                    Icon = "fa fa-phone",
                    Url = "/contact/phone"
                },
                new MenuItem()
                {
                    Text = "Drafts",
                    Icon = "fa fa-phone",
                    Url = "/contact/phone"
                },
                new MenuItem()
                {
                    Text = "Archives",
                    Icon = "fa fa-phone",
                    Url = "/contact/phone"
                },
            }
        },
        new MenuItem()
        {
            Text = "Templates",
            Icon = "fa fa-info-circle",
            Url = "/about"
        },
        new MenuItem()
        {
            Text = "Contracts",
            Icon = "fa fa-envelope",
            Url = "/contact",
        }
    };

}
