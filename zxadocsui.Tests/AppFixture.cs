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
        // Headed on demand: ZXADOCS_HEADED=1 shows the browser, with a slight delay so a person
        // can follow what the test is doing. Headless stays the default for routine runs.
        var headed = Environment.GetEnvironmentVariable("ZXADOCS_HEADED") is "1" or "true";
        Browser = await playwright.Chromium.LaunchAsync(new()
        {
            Channel = "chrome",
            Headless = !headed,
            SlowMo = headed ? 250 : 0,
        });
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

    /// <summary>
    /// An account holding ManageRole, read from ZXADOCS_ROLE_ADMIN and ZXADOCS_ROLE_ADMIN_PASSWORD,
    /// or null when they are not set. The default login does not hold it, so nothing it does can
    /// reach the category-field editor (ZD-126). The password stays in the environment because
    /// this file is committed.
    /// </summary>
    public static (string User, string Password)? RoleAdmin
    {
        get
        {
            var user = Environment.GetEnvironmentVariable("ZXADOCS_ROLE_ADMIN");
            var password = Environment.GetEnvironmentVariable("ZXADOCS_ROLE_ADMIN_PASSWORD");
            return string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password)
                ? null
                : (user, password);
        }
    }

    /// <summary>
    /// A system user, read from ZXADOCS_SYSTEM_ADMIN and ZXADOCS_SYSTEM_ADMIN_PASSWORD, or null
    /// when they are not set. No organisation user can reach the Settings section (ZD-131), and the
    /// account is provisioned at startup rather than through the product, so it cannot be derived
    /// from the other credentials here. The password stays in the environment because this file is
    /// committed.
    /// </summary>
    public static (string User, string Password)? SystemAdmin
    {
        get
        {
            var user = Environment.GetEnvironmentVariable("ZXADOCS_SYSTEM_ADMIN");
            var password = Environment.GetEnvironmentVariable("ZXADOCS_SYSTEM_ADMIN_PASSWORD");
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

        // The fields carry no label or aria-label, so they are reached by input type.
        await page.Locator("input[type='text']").First.FillAsync(username);
        var passwordField = page.Locator("input[type='password']").First;
        await passwordField.FillAsync(password);

        // The submit button is gated on MudForm validity, and filling a field does not by itself
        // run validation — pressing Enter is what triggers it. This used to be unnecessary because
        // the form arrived pre-filled and therefore already valid; ZD-107 removed those defaults.
        await passwordField.PressAsync("Enter");

        var login = page.GetByRole(AriaRole.Button, new() { Name = "Login" });
        await Assertions.Expect(login).ToBeEnabledAsync(new() { Timeout = 15_000 });
        await login.ClickAsync(new() { Timeout = 15_000 });

        await ChooseRoleIfAskedAsync(page);
        await page.WaitForURLAsync("**/dashboard", new() { Timeout = 30_000 });

        return page;
    }

    /// <summary>
    /// A user with more than one role is asked which to sign in as (Login.razor.cs, the side
    /// dialog). One role goes straight through, so this only fires for the multi-role accounts -
    /// and without it their login simply never reaches the dashboard.
    /// </summary>
    private static async Task ChooseRoleIfAskedAsync(IPage page)
    {
        var roles = page.Locator(".mud-list-item");
        try
        {
            await roles.First.WaitForAsync(new() { Timeout = 6_000 });
        }
        catch (TimeoutException)
        {
            return; // single-role account: no dialog, already on its way
        }

        // Whichever role is first. Which one is chosen does not matter to any test here; that it
        // gets chosen at all does.
        await roles.First.ClickAsync(new() { Timeout = 10_000 });
    }

    /// <summary>
    /// A page already signed in and sitting on the dashboard, as the default authoring account.
    ///
    /// The credentials are typed rather than inherited: ZD-107 cleared the hardcoded defaults the
    /// login form used to arrive with, because they prefilled a real password box in every
    /// environment the app was deployed to. These are the documented test credentials.
    /// </summary>
    public Task<IPage> SignedInPageAsync() => SignedInPageAsync(DefaultUser, DefaultPassword);

    /// <summary>The default authoring account: holds neither approve permission. See Approver.</summary>
    public const string DefaultUser = "pkavule";
    public const string DefaultPassword = "1234..34";

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
