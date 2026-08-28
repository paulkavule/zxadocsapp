using Microsoft.Playwright;

namespace zxadocsui.Tests;

// The reset page the emailed link opens (ZD-106). Every test here runs signed OUT, which is the
// state the page must work in.
//
// The valid-token path is not automated: the temporary password exists only in the outgoing email,
// and ZD-100/ZD-109 redact both it and the token from the notification log and every ILogger line
// on purpose. Nothing a browser can reach exposes a live token, so that path is covered by the
// end-to-end HTTP pass on ZD-103 instead.
[Collection("app")]
[Trait("Category", "Ui")]
public class PasswordResetPageTests(AppFixture app)
{
    private const string CompliantPassword = "BrandNewPass1!";

    private void SkipIfAppDown() =>
        Assert.SkipWhen(!app.AppReachable, app.SkipReason ?? "app not running");

    [Fact]
    public async Task The_page_opens_while_signed_out()
    {
        SkipIfAppDown();
        await using var page = await AnonymousPageAsync();

        await page.GotoAsync($"{AppFixture.UiBase}/reset-password?token=any-token-shape",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        // No redirect to the sign-in page: the visitor holds no token pair yet, by definition.
        Assert.Contains("/reset-password", page.Url);
        await VisibleAsync(page.GetByText("Choose your password"), 30_000);
    }

    [Fact]
    public async Task With_no_token_the_form_is_not_rendered_at_all()
    {
        SkipIfAppDown();
        await using var page = await AnonymousPageAsync();

        await page.GotoAsync($"{AppFixture.UiBase}/reset-password",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await VisibleAsync(page.GetByText("This link is invalid or has expired"), 30_000);

        // Not an empty form and not a stack trace: there is nothing to submit a password against.
        Assert.Equal(0, await page.Locator("input[type='password']").CountAsync());
        await VisibleAsync(page.GetByRole(AriaRole.Link, new() { Name = "Back to sign in" }), 10_000);
    }

    [Fact]
    public async Task A_token_the_server_refuses_ends_on_the_invalid_link_state()
    {
        SkipIfAppDown();
        await using var page = await AnonymousPageAsync();

        await page.GotoAsync($"{AppFixture.UiBase}/reset-password?token=never-was-issued",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        await FillBothAsync(page, CompliantPassword, CompliantPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Set password and continue" })
            .ClickAsync(new() { Timeout = 15_000 });

        // The server returns one message for unknown, used and expired; the page shows it as-is
        // and stops offering the form, because a refused token cannot become valid.
        await VisibleAsync(page.GetByText("This link is invalid or has expired"), 30_000);
        Assert.Equal(0, await page.Locator("input[type='password']").CountAsync());
        Assert.Contains("/reset-password", page.Url);
    }

    [Fact]
    public async Task A_mismatched_confirmation_is_refused_without_reaching_the_server()
    {
        SkipIfAppDown();
        await using var page = await AnonymousPageAsync();

        await page.GotoAsync($"{AppFixture.UiBase}/reset-password?token=some-token",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        await FillBothAsync(page, CompliantPassword, "SomethingElse1!");
        await page.GetByRole(AriaRole.Button, new() { Name = "Set password and continue" })
            .ClickAsync(new() { Timeout = 15_000 });

        await VisibleAsync(page.GetByText("The two passwords do not match"), 20_000);
        await AssertNotSubmittedAsync(page);
    }

    [Theory]
    [InlineData("short1A", "Use at least 8 characters")]
    [InlineData("alllowercase1", "Include an upper-case letter")]
    [InlineData("ALLUPPERCASE1", "Include a lower-case letter")]
    [InlineData("NoDigitsAtAll", "Include a digit")]
    public async Task A_password_below_the_policy_is_refused_client_side(string password, string expected)
    {
        SkipIfAppDown();
        await using var page = await AnonymousPageAsync();

        await page.GotoAsync($"{AppFixture.UiBase}/reset-password?token=some-token",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        await FillBothAsync(page, password, password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Set password and continue" })
            .ClickAsync(new() { Timeout = 15_000 });

        await VisibleAsync(page.GetByText(expected), 20_000);
        await AssertNotSubmittedAsync(page);
    }

    [Fact]
    public async Task The_token_is_never_written_into_the_page()
    {
        SkipIfAppDown();
        const string token = "zd106-token-that-must-not-be-rendered";
        await using var page = await AnonymousPageAsync();

        await page.GotoAsync($"{AppFixture.UiBase}/reset-password?token={token}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await VisibleAsync(page.GetByText("Choose your password"), 30_000);

        // It is a live credential; the query string is unavoidable, the body is not.
        var body = await page.Locator("body").InnerTextAsync();
        Assert.DoesNotContain(token, body);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A browser context with no session, which is how this page is always reached.</summary>
    private async Task<IPage> AnonymousPageAsync()
    {
        var context = await app.Browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true,           // dev certificate
            ViewportSize = new() { Width = 1600, Height = 1200 },
        });
        return await context.NewPageAsync();
    }

    private static async Task FillBothAsync(IPage page, string password, string confirm)
    {
        var fields = page.Locator("input[type='password']");
        await fields.First.WaitForAsync(new() { Timeout = 30_000 });

        await fields.Nth(0).FillAsync(password);
        await fields.Nth(1).FillAsync(confirm);

        // Immediate="true" validates per keystroke; give the last one a chance to land.
        await page.WaitForTimeoutAsync(600);
    }

    /// <summary>
    /// Proves the submit never reached the server, without watching for an HTTP request: this is
    /// Blazor Server, so the POST is made by the circuit and is invisible to the browser. The token
    /// used by these tests is one the server always refuses, so had the request gone out the page
    /// would have withdrawn the form and shown the invalid-link message. Both still being here is
    /// the evidence that client-side validation stopped it.
    /// </summary>
    private static async Task AssertNotSubmittedAsync(IPage page)
    {
        await page.WaitForTimeoutAsync(2500);

        Assert.Equal(2, await page.Locator("input[type='password']").CountAsync());
        Assert.Equal(0, await page.GetByText("This link is invalid or has expired").CountAsync());
    }

    private static async Task VisibleAsync(ILocator locator, int timeout) =>
        await locator.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeout });
}
