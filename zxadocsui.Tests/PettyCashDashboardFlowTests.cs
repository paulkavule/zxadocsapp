using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace zxadocsui.Tests;

// ZD-216 in a real browser: /dashboard shows each user only the petty cash panels their permissions allow,
// with the figures the API returns. Needs the API and UI running.
[Collection("app")]
[Trait("Category", "PettyCashE2E")]
public class PettyCashDashboardFlowTests(AppFixture app)
{
    private const string Password = "1234..34";

    [Theory]
    [InlineData("dkavule", "My wallet", "Awaiting my approval")]               // maker
    [InlineData("hbakileke", "Awaiting my approval", "My wallet")]             // approver
    [InlineData("jzake", "Open reconciliation exceptions", "My wallet")]       // finance viewer
    [InlineData("rnanzili", "Low balances", "Open reconciliation exceptions")] // finance administrator
    public async Task Each_role_sees_its_own_panels_only(string user, string shown, string hidden)
    {
        Assert.SkipWhen(!app.AppReachable, app.SkipReason ?? "app not running");
        await using var page = await app.SignedInPageAsync(user, Password);
        await page.GotoAsync($"{AppFixture.UiBase}/dashboard", new() { WaitUntil = WaitUntilState.NetworkIdle });

        await Assertions.Expect(page.GetByText(shown, new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Assertions.Expect(page.GetByText(hidden, new() { Exact = true })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task The_approver_panel_shows_the_queue_the_api_returns()
    {
        Assert.SkipWhen(!app.AppReachable, app.SkipReason ?? "app not running");
        var approver = (await DataAsync("hbakileke", "/api/petty-cash/dashboard")).GetProperty("approver");

        await using var page = await app.SignedInPageAsync("hbakileke", Password);
        await page.GotoAsync($"{AppFixture.UiBase}/dashboard", new() { WaitUntil = WaitUntilState.NetworkIdle });

        var panel = page.Locator("div.rounded-2xl", new() { Has = page.GetByText("Awaiting my approval", new() { Exact = true }) }).Last;
        await Assertions.Expect(panel).ToContainTextAsync($"{approver.GetProperty("count").GetInt32()} worth {approver.GetProperty("total").GetDecimal():N0} UGX",
            new() { Timeout = 20_000 });
    }

    private static async Task<JsonElement> DataAsync(string username, string path)
    {
        var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(AppFixture.ApiBase) };
        var login = await http.PostAsync("/api/user/login",
            new StringContent(JsonSerializer.Serialize(new { username, password = Password }), Encoding.UTF8, "application/json"));
        using (var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync()))
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", body.RootElement.GetProperty("data").GetProperty("token").GetString());
        using var data = JsonDocument.Parse(await http.GetStringAsync(path));
        return data.RootElement.GetProperty("data").Clone();
    }
}
