using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace zxadocsui.Tests;

// ZD-322 in a real browser: an accountant resolves an open reconciliation exception with evidence.
// Needs the API and UI running and at least one open exception; opening one needs a reconciliation
// run, which needs the payment service, so with none open the test skips rather than fails.
[Collection("app")]
[Trait("Category", "PettyCashE2E")]
public class ReconciliationResolveFlowTests(AppFixture app)
{
    private const string Password = "1234..34";
    private const string Accountant = "rkyolaba";   // ManageAccounting
    private const string Maker = "dkavule";         // no ManageAccounting

    [Fact]
    public async Task An_accountant_resolves_an_open_exception_with_evidence_and_it_stays_in_history()
    {
        Assert.SkipWhen(!app.AppReachable, app.SkipReason ?? "app not running");
        using var api = await ApiAsync(Accountant);
        var open = await DataAsync(api, "/api/accounting/reconciliation/exceptions?status=1");
        Assert.SkipWhen(open.Count == 0, "No open reconciliation exception; run reconciliation with the payment service up");
        var id = open[0].GetProperty("id").GetInt32();
        var resolveUrl = $"/api/accounting/reconciliation/exceptions/{id}/resolve";

        using (var maker = await ApiAsync(Maker))
            Assert.Equal(HttpStatusCode.Forbidden, (await maker.PostAsync(resolveUrl, Form("1", "Not mine to close"))).StatusCode);

        await using var page = await app.SignedInPageAsync(Accountant, Password);
        await page.GotoAsync($"{AppFixture.UiBase}/accounting/reconciliation/{id}", new() { WaitUntil = WaitUntilState.NetworkIdle });
        var resolve = page.GetByRole(AriaRole.Button, new() { Name = "Resolve", Exact = true });

        await resolve.ClickAsync();
        await ExpectSnackbarAsync(page, "Choose a resolution type and give a comment.");

        await ChooseAsync(page, "Resolution type", "Explained");
        await page.Locator("textarea").FillAsync("Provider fee netted; statement attached");
        await resolve.ClickAsync();
        await ExpectSnackbarAsync(page, "An Explained resolution needs an evidence file");

        await page.Locator("input[type=file]").SetInputFilesAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "import-sample.pdf"));
        await Assertions.Expect(page.GetByText("import-sample.pdf")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await resolve.ClickAsync();
        await ExpectSnackbarAsync(page, "Exception resolved.");

        var resolution = page.Locator("section", new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = "Resolution" }) });
        await Assertions.Expect(resolution).ToContainTextAsync("Explained", new() { Timeout = 15_000 });
        await Assertions.Expect(resolution).ToContainTextAsync("Provider fee netted; statement attached");
        await Assertions.Expect(resolve).ToHaveCountAsync(0);

        var download = await page.RunAndWaitForDownloadAsync(() => resolution.GetByText("import-sample.pdf").ClickAsync());
        Assert.Equal("import-sample.pdf", download.SuggestedFilename);

        var history = await DataAsync(api, "/api/accounting/reconciliation/exceptions?status=2");
        var row = history.Single(e => e.GetProperty("id").GetInt32() == id);
        Assert.Equal("import-sample.pdf", row.GetProperty("evidenceFileName").GetString());
        Assert.Contains(row.GetProperty("resolutionType").ToString(), new[] { "1", "Explained" });
        Assert.NotEmpty(row.GetProperty("resolvedBy").GetString()!);
        Assert.Equal(HttpStatusCode.Conflict, (await api.PostAsync(resolveUrl, Form("3", "Second close"))).StatusCode);
    }

    private static MultipartFormDataContent Form(string type, string comment) =>
        new() { { new StringContent(type), "type" }, { new StringContent(comment), "comment" } };

    // MudSelect renders id="false" on the input, so GetByLabel cannot resolve it.
    private static async Task ChooseAsync(IPage page, string label, string option)
    {
        await page.Locator($"div.mud-input-control:has(label:text-is('{label}'))").ClickAsync();
        await page.Locator("div.mud-popover-open .mud-list-item", new() { HasTextString = option }).First.ClickAsync();
    }

    private static Task ExpectSnackbarAsync(IPage page, string text) =>
        Assertions.Expect(page.Locator("div.mud-snackbar", new() { HasTextString = text }).First).ToBeVisibleAsync(new() { Timeout = 20_000 });

    private static async Task<List<JsonElement>> DataAsync(HttpClient api, string path)
    {
        using var body = JsonDocument.Parse(await api.GetStringAsync(path));
        return body.RootElement.GetProperty("data").EnumerateArray().Select(e => e.Clone()).ToList();
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
