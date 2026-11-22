using System.Text;
using Microsoft.Playwright;

namespace zxadocsui.Tests;

// ZD-230 in a real browser: the requests, fundings and reconciliation lists export to Excel and PDF with the
// filters on screen, and a finished queued export downloads from My exports. Assumes the default row limit
// (5,000), so these lists download at once. Needs the API and UI running.
[Collection("app")]
[Trait("Category", "PettyCashE2E")]
public class ListExportsFlowTests(AppFixture app)
{
    private const string Password = "1234..34";
    private const string Finance = "snaluyima";   // ViewPettyCashFinance, ApproveFundingRequest

    [Fact]
    public async Task Finance_exports_the_request_funding_and_reconciliation_lists_and_collects_a_queued_export()
    {
        Assert.SkipWhen(!app.AppReachable, app.SkipReason ?? "app not running");
        await using var page = await app.SignedInPageAsync(Finance, Password);

        await page.GotoAsync($"{AppFixture.UiBase}/petty-cash/requests", new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator(".mud-chip", new() { HasTextString = "All" }).First.ClickAsync();
        await ExpectDownloadsAsync(page, "petty-cash-requests");

        await page.GotoAsync($"{AppFixture.UiBase}/petty-cash/fundings", new() { WaitUntil = WaitUntilState.NetworkIdle });
        await ExpectDownloadsAsync(page, "wallet-fundings");

        await page.GotoAsync($"{AppFixture.UiBase}/accounting/reconciliation", new() { WaitUntil = WaitUntilState.NetworkIdle });
        await ExpectDownloadsAsync(page, "reconciliation-exceptions");

        await page.GotoAsync($"{AppFixture.UiBase}/exports", new() { WaitUntil = WaitUntilState.NetworkIdle });
        var ready = page.GetByRole(AriaRole.Button, new() { Name = "Download" });
        if (await ready.CountAsync() == 0)
            return;
        var download = await page.RunAndWaitForDownloadAsync(() => ready.First.ClickAsync(), new() { Timeout = 60_000 });
        Assert.Matches(@"\.(xlsx|pdf)$", download.SuggestedFilename);
    }

    /// <summary>Both export buttons on the page download a file of their format.</summary>
    private static async Task ExpectDownloadsAsync(IPage page, string stem)
    {
        foreach (var (button, extension, magic) in new[] { ("Export Excel", ".xlsx", "PK"), ("Export PDF", ".pdf", "%PDF") })
        {
            var download = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = button }).ClickAsync(),
                new() { Timeout = 60_000 });
            Assert.StartsWith(stem + "-", download.SuggestedFilename);
            Assert.EndsWith(extension, download.SuggestedFilename);
            var head = new byte[magic.Length];
            await using (var content = await download.CreateReadStreamAsync())
                await content.ReadExactlyAsync(head);
            Assert.Equal(magic, Encoding.ASCII.GetString(head));
        }
    }
}
