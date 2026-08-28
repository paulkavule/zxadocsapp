using Microsoft.Playwright;

namespace zxadocsui.Tests;

// The sign-in page after ZD-107: it opens empty, and "Forgot password?" reports the same thing
// whether or not the account exists.
//
// Note on what cannot be asserted here: this is Blazor Server, so IHttpService calls the API from
// the circuit. The browser never issues those requests, and watching page.Request for them would
// pass whatever the app did. Assertions below rest on user-visible outcomes instead.
//
// The pending-user redirect is not automated here. Reaching it needs a sign-in with a temporary
// password, and that password exists only in the outgoing email — ZD-100/ZD-109 redact it from the
// notification log and every ILogger line on purpose. See the note on ZD-106.
[Collection("app")]
[Trait("Category", "Ui")]
public class LoginFlowTests(AppFixture app)
{
    private void SkipIfAppDown() =>
        Assert.SkipWhen(!app.AppReachable, app.SkipReason ?? "app not running");

    [Fact]
    public async Task The_sign_in_form_opens_empty()
    {
        SkipIfAppDown();
        await using var page = await AnonymousPageAsync();

        await page.GotoAsync(AppFixture.UiBase, new() { WaitUntil = WaitUntilState.NetworkIdle });

        // The form used to arrive carrying a real username and password, in every environment the
        // app was deployed to.
        Assert.Equal(string.Empty, await page.Locator("input[type='text']").First.InputValueAsync());
        Assert.Equal(string.Empty, await page.Locator("input[type='password']").First.InputValueAsync());
    }

    [Fact]
    public async Task The_reset_request_confirmation_is_identical_for_a_real_and_an_unknown_account()
    {
        SkipIfAppDown();

        var real = await RequestResetAsync(AppFixture.DefaultUser);
        var unknown = await RequestResetAsync("definitely-no-such-account");

        // Anything account-specific here would undo the endpoint's non-enumerating design.
        Assert.Equal(real, unknown);
        Assert.Contains("if that account exists", real, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_forgot_panel_does_not_block_signing_in()
    {
        SkipIfAppDown();
        await using var page = await AnonymousPageAsync();

        await page.GotoAsync(AppFixture.UiBase, new() { WaitUntil = WaitUntilState.NetworkIdle });
        await OpenForgotPanelAsync(page);

        // The panel sits outside the MudForm on purpose: a field inside it would join login
        // validation and could leave the Login button disabled.
        await page.Locator("input[type='text']").First.FillAsync(AppFixture.DefaultUser);
        var password = page.Locator("input[type='password']").First;
        await password.FillAsync(AppFixture.DefaultPassword);

        // The button is gated on MudForm validity, which only becomes true once validation has
        // run — pressing Enter in a field is what triggers it.
        await password.PressAsync("Enter");

        var login = page.GetByRole(AriaRole.Button, new() { Name = "Login" });
        await login.WaitForAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(login).ToBeEnabledAsync(new() { Timeout = 15_000 });
        await login.ClickAsync(new() { Timeout = 15_000 });

        await page.WaitForURLAsync("**/dashboard", new() { Timeout = 30_000 });
        Assert.Contains("/dashboard", page.Url);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Asks for a reset link and returns the confirmation the visitor is shown.</summary>
    private async Task<string> RequestResetAsync(string identifier)
    {
        await using var page = await AnonymousPageAsync();
        await page.GotoAsync(AppFixture.UiBase, new() { WaitUntil = WaitUntilState.NetworkIdle });

        await OpenForgotPanelAsync(page);
        await page.GetByPlaceholder("Username or email").FillAsync(identifier);
        await page.GetByRole(AriaRole.Button, new() { Name = "Send reset link" })
            .ClickAsync(new() { Timeout = 15_000 });

        var snackbar = page.Locator("div.mud-snackbar");
        await snackbar.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        return (await snackbar.First.InnerTextAsync()).Trim();
    }

    private static async Task OpenForgotPanelAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Forgot password?" })
            .ClickAsync(new() { Timeout = 30_000 });
        await page.GetByPlaceholder("Username or email").WaitForAsync(new() { Timeout = 15_000 });
    }

    private async Task<IPage> AnonymousPageAsync()
    {
        var context = await app.Browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true,           // dev certificate
            ViewportSize = new() { Width = 1600, Height = 1200 },
        });
        return await context.NewPageAsync();
    }
}
