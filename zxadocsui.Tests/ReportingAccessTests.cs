using Microsoft.Playwright;

namespace zxadocsui.Tests;

// ZD-133. The document activity report was open to every authenticated user: the whole
// organisation's action history, whether or not the documents were anything to do with them.
// It now needs the organisation's reporting permission, or the System Viewer role.
[Collection("app")]
[Trait("Category", "Ui")]
public class ReportingAccessTests(AppFixture app)
{
    private void SkipIfAppDown() =>
        Assert.SkipWhen(!app.AppReachable, app.SkipReason ?? "app not running");

    [Fact]
    public async Task A_user_without_reporting_access_sees_no_reports_section()
    {
        SkipIfAppDown();
        await using var page = await app.SignedInPageAsync();

        // The nav is built after the permission fetch, so give that render a moment to land.
        await page.WaitForTimeoutAsync(3000);

        await Assertions.Expect(page.Locator("#reportsToggle")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("a[href='/reports/activity']")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task A_user_without_reporting_access_deep_linking_into_the_report_is_bounced()
    {
        SkipIfAppDown();
        await using var page = await app.SignedInPageAsync();

        await page.GotoAsync($"{AppFixture.UiBase}/reports/activity",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.WaitForTimeoutAsync(4000);

        // Client-side redirect only; the endpoint refuses the call regardless of what renders.
        Assert.EndsWith("/dashboard", page.Url.TrimEnd('/'));
    }

    [Fact]
    public async Task A_system_viewer_reaches_the_report()
    {
        SkipIfAppDown();
        var admin = AppFixture.SystemAdmin;
        Assert.SkipWhen(admin is null, "ZXADOCS_SYSTEM_ADMIN/_PASSWORD are not set");

        await using var page = await app.SignedInPageAsync(admin!.Value.User, admin.Value.Password);
        await page.WaitForTimeoutAsync(3000);

        var link = page.Locator("a[href='/reports/activity']");
        Assert.SkipWhen(await link.CountAsync() == 0, "this account holds no System Viewer role");

        await page.GotoAsync($"{AppFixture.UiBase}/reports/activity",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.WaitForTimeoutAsync(4000);

        Assert.Contains("/reports/activity", page.Url);
        // Scoped to the heading: the sidebar link matches the same text.
        await Assertions.Expect(page.Locator("h1:text-is('Document Activity')"))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task The_organisation_column_appears_only_when_the_report_spans_all_of_them()
    {
        SkipIfAppDown();
        var admin = AppFixture.SystemAdmin;
        Assert.SkipWhen(admin is null, "ZXADOCS_SYSTEM_ADMIN/_PASSWORD are not set");

        await using var page = await app.SignedInPageAsync(admin!.Value.User, admin.Value.Password);
        await page.GotoAsync($"{AppFixture.UiBase}/reports/activity",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.WaitForTimeoutAsync(4000);
        Assert.SkipWhen(!page.Url.Contains("/reports/activity"), "this account holds no System Viewer role");

        // On one organisation the column would repeat the same value on every row, so it is not shown.
        var column = page.Locator("th", new() { HasTextString = "Organisation" });
        await Assertions.Expect(column).ToHaveCountAsync(0);

        await page.GetByLabel("Acting on").ClickAsync();
        var options = page.Locator("div.mud-popover-open .mud-list-item");
        await options.First.WaitForAsync(new() { Timeout = 15_000 });
        await options.Filter(new() { HasTextString = "All organisations" }).First.ClickAsync(new() { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(3000);

        await Assertions.Expect(column.First).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
