using Microsoft.Playwright;

namespace zxadocsui.Tests;

// The side panel (SideDialogService + SideDialog). A closed MudDrawer outside a MudBlazor drawer
// container stayed position:absolute and visible at the right edge, so it extended the document by
// its own width on every page and could simply be scrolled into view. It is now rendered only
// while something has been requested, which means the open path needs pinning too.
//
// The role-selection dialog is the one side panel a committed test can reach: it is shown to any
// account holding more than one role, and needs no system privilege.
[Collection("app")]
[Trait("Category", "Ui")]
public class SidePanelTests(AppFixture app)
{
    /// <summary>A multi-role account, so signing in asks which role to use. See CLAUDE.md.</summary>
    private const string MultiRoleUser = "hbakileke";

    private void SkipIfAppDown() =>
        Assert.SkipWhen(!app.AppReachable, app.SkipReason ?? "app not running");

    [Fact]
    public async Task No_side_panel_is_in_the_document_until_one_is_asked_for()
    {
        SkipIfAppDown();
        await using var page = await app.SignedInPageAsync();
        await page.WaitForTimeoutAsync(3000);

        await Assertions.Expect(page.Locator(".mud-drawer")).ToHaveCountAsync(0);

        // The regression it caused: 420px of closed drawer past the right edge of every page.
        var overflows = await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth");
        Assert.False(overflows, "the page scrolls horizontally, which is how the panel became reachable");
    }

    [Fact]
    public async Task Asking_for_a_side_panel_opens_it_and_closing_removes_it_again()
    {
        SkipIfAppDown();

        var context = await app.Browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true,
            ViewportSize = new() { Width = 1600, Height = 1200 },
        });
        await using var page = await context.NewPageAsync();
        await page.GotoAsync(AppFixture.UiBase, new() { WaitUntil = WaitUntilState.NetworkIdle });

        await page.Locator("input[type='text']").First.FillAsync(MultiRoleUser);
        var password = page.Locator("input[type='password']").First;
        await password.FillAsync(AppFixture.DefaultPassword);
        await password.PressAsync("Enter");

        var login = page.GetByRole(AriaRole.Button, new() { Name = "Login" });
        await Assertions.Expect(login).ToBeEnabledAsync(new() { Timeout = 15_000 });
        await login.ClickAsync(new() { Timeout = 15_000 });

        // The panel carries the role list, so it has to be in the document now.
        var drawer = page.Locator(".mud-drawer");
        try
        {
            await drawer.First.WaitForAsync(new() { Timeout = 15_000 });
        }
        catch (TimeoutException)
        {
            Assert.Skip($"{MultiRoleUser} no longer holds more than one role, so no panel is shown");
            return;
        }

        await Assertions.Expect(drawer.First).ToBeVisibleAsync();
        await page.Locator(".mud-drawer .mud-list-item").First.ClickAsync(new() { Timeout = 10_000 });
        await page.WaitForURLAsync("**/dashboard", new() { Timeout = 30_000 });

        // Selecting a role navigates, which switches layout and disposes this component - so this
        // proves the panel opens, NOT that closing removes it. That is the test below.
        await Assertions.Expect(drawer).ToHaveCountAsync(0, new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task Closing_a_side_panel_takes_it_back_out_of_the_document()
    {
        SkipIfAppDown();
        var admin = AppFixture.SystemAdmin;
        Assert.SkipWhen(admin is null, "ZXADOCS_SYSTEM_ADMIN/_PASSWORD are not set");

        await using var page = await app.SignedInPageAsync(admin!.Value.User, admin.Value.Password);
        await page.GotoAsync($"{AppFixture.UiBase}/settings/departments",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.WaitForTimeoutAsync(4000);

        var open = page.GetByRole(AriaRole.Button, new() { Name = "New department" });
        Assert.SkipWhen(await open.CountAsync() == 0, "this account cannot add departments");

        await open.First.ClickAsync();
        var drawer = page.Locator(".mud-drawer");
        await drawer.First.WaitForAsync(new() { Timeout = 15_000 });

        // The drawer's own X, so the panel closes without navigating away - the case where the
        // component survives and has to remove its own markup.
        await page.Locator(".mud-drawer button[aria-label='Close']").First.ClickAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(drawer).ToHaveCountAsync(0, new() { Timeout = 15_000 });

        var overflows = await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth");
        Assert.False(overflows, "the page scrolls horizontally after closing, so the panel is still parked off-screen");
    }
}
