using Microsoft.Playwright;

namespace zxadocsui.Tests;

// Drives the running app in a real browser. The editor and the four preview surfaces have no other
// coverage: they are Quill, a sandboxed iframe and Blazor CSS isolation interacting, none of which
// can be exercised out of process.
//
// Uses the INSTALLED Chrome (Channel = "chrome") rather than downloading Chromium, because
// installing Playwright's browsers needs pwsh, which is not present on the dev machines here.
public sealed class AppFixture : IAsyncLifetime
{
    public const string UiBase = "https://localhost:7086";
    public const string ApiBase = "https://localhost:7028";

    private IPlaywright? playwright;
    public IBrowser Browser { get; private set; } = default!;
    public bool AppReachable { get; private set; }
    public string? SkipReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        using var probe = new HttpClientHandler
        {
            // The dev certificate; the app is only ever reached on localhost here.
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        using var http = new HttpClient(probe) { Timeout = TimeSpan.FromSeconds(5) };

        foreach (var url in new[] { UiBase, ApiBase + "/swagger/index.html" })
        {
            try
            {
                var response = await http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    SkipReason = $"{url} returned {(int)response.StatusCode}";
                    return;
                }
            }
            catch (Exception ex)
            {
                SkipReason = $"{url} is not reachable ({ex.GetType().Name}). " +
                             "Start both apps first — scripts/verify-render.sh does this for you.";
                return;
            }
        }

        playwright = await Playwright.CreateAsync();
        Browser = await playwright.Chromium.LaunchAsync(new() { Channel = "chrome", Headless = true });
        AppReachable = true;
    }

    /// <summary>
    /// An account holding ApproveTemplate/ApproveDraft, read from ZXADOCS_APPROVER and
    /// ZXADOCS_APPROVER_PASSWORD, or null when they are not set.
    ///
    /// A second actor is unavoidable here: the app's dev-default login — the user every other test
    /// runs as — holds ViewTemplates/CreateTemplate/ViewDrafts/CreateDraft and NEITHER approve
    /// permission, so nothing it authors can reach the approved state the draft surfaces require.
    /// The password stays in the environment because this file is committed.
    /// </summary>
    public static (string User, string Password)? Approver
    {
        get
        {
            var user = Environment.GetEnvironmentVariable("ZXADOCS_APPROVER");
            var password = Environment.GetEnvironmentVariable("ZXADOCS_APPROVER_PASSWORD");
            return string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password)
                ? null
                : (user, password);
        }
    }

    /// <summary>A page signed in as a named user, for the flows that need a second actor.</summary>
    public async Task<IPage> SignedInPageAsync(string username, string password)
    {
        var context = await Browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true,
            ViewportSize = new() { Width = 1600, Height = 1200 },
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(UiBase, new() { WaitUntil = WaitUntilState.NetworkIdle });

        // The fields carry no label or aria-label, so they are reached by input type. Filling
        // replaces the development defaults the form arrives with.
        await page.Locator("input[type='text']").First.FillAsync(username);
        await page.Locator("input[type='password']").First.FillAsync(password);

        await page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync(new() { Timeout = 15_000 });
        await page.WaitForURLAsync("**/dashboard", new() { Timeout = 30_000 });

        return page;
    }

    /// <summary>A page already signed in and sitting on the dashboard.</summary>
    public async Task<IPage> SignedInPageAsync()
    {
        var context = await Browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true,           // dev certificate
            ViewportSize = new() { Width = 1600, Height = 1200 },
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync(UiBase, new() { WaitUntil = WaitUntilState.NetworkIdle });

        // The login form arrives pre-filled from the app's own development defaults; no credential
        // is typed here. The submit button is gated on MudForm validity, which only becomes true
        // once validation has run — pressing Enter in a field is what triggers it.
        var username = page.Locator("input[type='text']").First;
        await username.ClickAsync();
        await username.PressAsync("Enter");

        var login = page.GetByRole(AriaRole.Button, new() { Name = "Login" });
        await login.ClickAsync(new() { Timeout = 15_000 });
        await page.WaitForURLAsync("**/dashboard", new() { Timeout = 30_000 });

        return page;
    }

    /// <summary>Waits for the Quill editor on the current page to be live.</summary>
    public static async Task<ILocator> EditorAsync(IPage page)
    {
        var container = page.Locator(".zx-page .ql-container");
        await container.WaitForAsync(new() { Timeout = 30_000 });
        await page.WaitForFunctionAsync(
            "() => { const el = document.querySelector('.zx-page .ql-container');" +
            " return !!(el && window.Quill && window.Quill.find(el)); }",
            null, new() { Timeout = 30_000 });
        return container;
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null) await Browser.CloseAsync();
        playwright?.Dispose();
    }
}

[CollectionDefinition("app")]
public class AppCollection : ICollectionFixture<AppFixture>;
