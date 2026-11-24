using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace zxadocsui.Tests;

// ZD-255 in a real browser against whatever the organisation has posted: the trial balance shows the
// API's totals and balances, a request reference narrows the journal report and drills into the request,
// a billing unit's wallet ledger closes on the wallet's balance, and both exports download (ZD-339). Needs the API and UI running.
[Collection("app")]
[Trait("Category", "PettyCashE2E")]
public class AccountingReportsFlowTests(AppFixture app)
{
    private const string Password = "1234..34";
    private const string Accountant = "rkyolaba";   // ManageAccounting
    private const string Maker = "dkavule";         // no accounting permission

    [Fact]
    public async Task An_accountant_reads_balanced_reports_and_drills_into_a_source()
    {
        Assert.SkipWhen(!app.AppReachable, app.SkipReason ?? "app not running");
        using var api = await ApiAsync(Accountant);
        var tb = await DataAsync(api, "/api/accounting/reports/trial-balance");
        Assert.SkipWhen(tb.GetProperty("rows").GetArrayLength() == 0, "Nothing posted yet");
        Assert.Equal(tb.GetProperty("totalDebit").GetDecimal(), tb.GetProperty("totalCredit").GetDecimal());
        Assert.Equal(0m, tb.GetProperty("totalClosing").GetDecimal());

        using (var maker = await ApiAsync(Maker))
            Assert.Equal(HttpStatusCode.Forbidden, (await maker.GetAsync("/api/accounting/reports/trial-balance")).StatusCode);

        await using var page = await app.SignedInPageAsync(Accountant, Password);
        await page.GotoAsync($"{AppFixture.UiBase}/accounting/reports", new() { WaitUntil = WaitUntilState.NetworkIdle });

        await page.GetByRole(AriaRole.Tab, new() { Name = "Trial balance" }).ClickAsync();
        var totals = page.Locator("tfoot");
        await Assertions.Expect(totals).ToContainTextAsync(tb.GetProperty("totalDebit").GetDecimal().ToString("N2"), new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByText("The trial balance does not balance.")).ToHaveCountAsync(0);

        // ZD-339: both exports of the open tab download as files of their format.
        foreach (var (button, extension, magic) in new[] { ("Export Excel", ".xlsx", "PK"), ("Export PDF", ".pdf", "%PDF") })
        {
            var download = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = button }).ClickAsync(),
                new() { Timeout = 60_000 });
            Assert.StartsWith("trial-balance-", download.SuggestedFilename);
            Assert.EndsWith(extension, download.SuggestedFilename);
            var head = new byte[magic.Length];
            await using (var content = await download.CreateReadStreamAsync())
                await content.ReadExactlyAsync(head);
            Assert.Equal(magic, Encoding.ASCII.GetString(head));
        }

        var request = (await DataAsync(api, "/api/accounting/reports/journals")).EnumerateArray()
            .First(j => j.GetProperty("sourceType").GetString() == "PettyCashRequest");
        var reference = request.GetProperty("sourceReference").GetString()!;
        await page.GetByRole(AriaRole.Tab, new() { Name = "Journals" }).ClickAsync();
        await page.GetByLabel("Reference").FillAsync(reference);
        await page.GetByRole(AriaRole.Button, new() { Name = "Run report" }).ClickAsync();
        var link = page.GetByRole(AriaRole.Link, new() { Name = reference });
        // The unfiltered list already holds this reference, so wait for the filtered row count rather than the link.
        var filtered = (await DataAsync(api, $"/api/accounting/reports/journals?reference={reference}")).GetArrayLength();
        await Assertions.Expect(page.Locator("tbody tr")).ToHaveCountAsync(filtered, new() { Timeout = 15_000 });
        Assert.All(await page.Locator("tbody tr").AllInnerTextsAsync(), row => Assert.Contains(reference, row));

        var unitCode = request.GetProperty("billingUnitCode").GetString()!;
        await page.GetByRole(AriaRole.Tab, new() { Name = "Wallet ledger" }).ClickAsync();
        await page.Locator("div.mud-input-control:has(label:text-is('Billing unit (required)'))").ClickAsync();
        await page.Locator("div.mud-popover-open .mud-list-item", new() { HasTextString = unitCode }).First.ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Run report" }).ClickAsync();
        var summary = page.Locator("div.text-sm", new() { HasTextString = $"Wallet {unitCode} ·" });
        await Assertions.Expect(summary).ToBeVisibleAsync(new() { Timeout = 15_000 });
        var text = await summary.InnerTextAsync();
        var closing = Regex.Match(text, @"closing ([\d,\.\-]+)").Groups[1].Value;
        var current = Regex.Match(text, @"current ledger balance ([\d,\.\-]+)").Groups[1].Value;
        Assert.Equal(current, closing);

        await page.GetByRole(AriaRole.Tab, new() { Name = "Journals" }).ClickAsync();
        await link.First.ClickAsync();
        await page.WaitForURLAsync($"**/petty-cash/requests/{request.GetProperty("sourceId").GetInt32()}", new() { Timeout = 15_000 });
    }

    private static async Task<JsonElement> DataAsync(HttpClient api, string path)
    {
        using var body = JsonDocument.Parse(await api.GetStringAsync(path));
        return body.RootElement.GetProperty("data").Clone();
    }

    private static async Task<HttpClient> ApiAsync(string username)
    {
        var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        var http = new HttpClient(handler) { BaseAddress = new Uri(AppFixture.ApiBase) };
        var login = await http.PostAsync("/api/user/login",
            new StringContent(JsonSerializer.Serialize(new { username, password = Password }), Encoding.UTF8, "application/json"));
        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.RootElement.GetProperty("data").GetProperty("token").GetString());
        return http;
    }
}
