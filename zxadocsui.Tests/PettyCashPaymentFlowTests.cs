using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace zxadocsui.Tests;

// The petty cash payment flow in a real browser, from an empty accounting setup to money paid
// through the MTN sandbox. Needs the API, UI, zxadocsworker, the payment service and RabbitMQ
// running, and an active MTN sandbox provider account in the payment service with currency EUR:
// the sandbox rejects UGX as INVALID_CURRENCY and the payment then waits forever as unknown.
//
// Setup steps only act when something is missing, so the test can run repeatedly against the
// same organisation. Each actor holds only the permissions their step needs.
[Collection("app")]
[Trait("Category", "PettyCashE2E")]
public class PettyCashPaymentFlowTests(AppFixture app)
{
    private const string Password = "1234..34";
    private const string RoleAdmin = "lkatongole";       // ManageRole
    private const string Accountant = "rkyolaba";        // ManageAccounting
    private const string Maker = "dkavule";              // CreatePettyCashRequest, assigned to the Technology wallet
    private const string FundingApprover = "snaluyima";  // Finance Approver role
    private const string RequestApprover = "hbakileke";  // ApprovePettyCashRequest, same unit as the maker
    private const string MakerRole = "Billing Unit Filer";
    private const string FundingApproverRole = "Finance Approver";

    private static readonly TimeSpan PaymentWait = TimeSpan.FromMinutes(6);

    [Fact]
    public async Task An_approved_request_is_paid_through_the_mtn_sandbox()
    {
        Assert.SkipWhen(!app.AppReachable, app.SkipReason ?? "app not running");
        var run = DateTime.Now.ToString("MMddHHmmss");
        var amount = Random.Shared.Next(1_000, 9_999);
        var phone = "25677" + Random.Shared.Next(1_000_000, 9_999_999);

        await GrantFundingPermissionsAsync();
        await PrepareAccountingAsync();
        await FundWalletAsync($"E2E-{run}", 1_000_000);

        var requestId = await RaiseRequestAsync(amount, phone, $"E2E payment {run}");
        await ApproveRequestAsync(requestId);

        await WaitForPaymentAsync(requestId);

        await using var accountant = await SignInAsync(Accountant);
        await accountant.GotoAsync($"{AppFixture.UiBase}/accounting/journals", new() { WaitUntil = WaitUntilState.NetworkIdle });
        var requestJournals = accountant.Locator("tr", new() { HasTextString = $"PettyCashRequest #{requestId}" });
        await Assertions.Expect(requestJournals).ToHaveCountAsync(2, new() { Timeout = 30_000 });
    }

    /// <summary>Funding is requested by a wallet holder and approved by someone else (permissions 87 and 88).</summary>
    private async Task GrantFundingPermissionsAsync()
    {
        await using var page = await SignInAsync(RoleAdmin);
        foreach (var (role, permission) in new[] { (MakerRole, "Create funding request"), (FundingApproverRole, "Approve funding request") })
        {
            // Opened from the list: loading /roles/{id} directly redirects back to /roles.
            await page.GotoAsync($"{AppFixture.UiBase}/roles", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.Locator("tr", new() { HasTextString = role }).GetByLabel("Edit role").ClickAsync();
            var box = page.GetByLabel(permission, new() { Exact = true });
            await box.WaitForAsync(new() { Timeout = 30_000 });
            if (await box.IsCheckedAsync()) continue;

            await box.CheckAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
            await ExpectSnackbarAsync(page);
        }
    }

    /// <summary>Accounts, an open period and the Funding and Settlement rules the flow posts through.</summary>
    private async Task PrepareAccountingAsync()
    {
        using var api = await ApiAsync(Accountant);
        await using var page = await SignInAsync(Accountant);

        var accounts = await DataAsync(api, "/api/accounting/accounts");
        var codes = accounts.Select(a => a.GetProperty("code").GetString()).ToHashSet();
        foreach (var (code, name) in new[] { ("1000", "Bank"), ("1100", "Petty cash wallet") })
        {
            if (codes.Contains(code)) continue;
            await page.GotoAsync($"{AppFixture.UiBase}/accounting/accounts", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.GetByRole(AriaRole.Button, new() { Name = "New account" }).ClickAsync();
            await page.GetByLabel("Code").FillAsync(code);
            await page.GetByLabel("Name").FillAsync(name);
            await ChooseAsync(page, "Type", "Asset");
            await page.GetByRole(AriaRole.Button, new() { Name = "Add account" }).ClickAsync();
            await Assertions.Expect(page.Locator("tr", new() { HasTextString = name })).ToBeVisibleAsync(new() { Timeout = 15_000 });
        }

        await EnsureOpenPeriodAsync(api, page);

        var rules = await DataAsync(api, "/api/accounting/posting-rules");
        foreach (var (stage, debit, credit) in new[] { ("Funding", "1100", "1000"), ("Settlement", "2100", "1100") })
        {
            var current = rules.Any(r => StageName(r) == stage && r.GetProperty("effectiveTo").ValueKind == JsonValueKind.Null);
            if (current) continue;

            await page.GotoAsync($"{AppFixture.UiBase}/accounting/posting-rules", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.GetByRole(AriaRole.Button, new() { Name = "New rule" }).ClickAsync();
            await ChooseAsync(page, "Stage", stage);
            await ChooseAsync(page, "Debit account", debit);
            await ChooseAsync(page, "Credit account", credit);
            await page.GetByRole(AriaRole.Button, new() { Name = "Add rule" }).ClickAsync();
            await Assertions.Expect(page.Locator("tr", new() { HasTextString = stage }).Filter(new() { HasTextString = "current" }))
                .ToBeVisibleAsync(new() { Timeout = 15_000 });
        }
    }

    /// <summary>
    /// Journals post only into an open period covering their date, which the server takes in UTC.
    /// Opens a closed period covering today, or adds the current month when there is none.
    /// </summary>
    private static async Task EnsureOpenPeriodAsync(HttpClient api, IPage page)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var covering = (await DataAsync(api, "/api/accounting/periods")).FirstOrDefault(p =>
            DateOnly.Parse(p.GetProperty("startDate").GetString()!) <= today && today <= DateOnly.Parse(p.GetProperty("endDate").GetString()!));

        await page.GotoAsync($"{AppFixture.UiBase}/accounting/periods", new() { WaitUntil = WaitUntilState.NetworkIdle });
        if (covering.ValueKind != JsonValueKind.Undefined)
        {
            if (covering.GetProperty("isOpen").GetBoolean()) return;
            var row = page.Locator("tr", new() { HasTextString = covering.GetProperty("name").GetString()! });
            await row.Locator(".mud-switch input").CheckAsync();
            await Assertions.Expect(row).ToContainTextAsync("Open", new() { Timeout = 15_000 });
            return;
        }

        var name = today.ToString("MMM yyyy");
        await page.GetByRole(AriaRole.Button, new() { Name = "New period" }).ClickAsync();
        await page.GetByLabel("Name").FillAsync(name);
        await PickDayAsync(page, "From", 1);
        await PickDayAsync(page, "To", DateTime.DaysInMonth(today.Year, today.Month));
        await page.GetByRole(AriaRole.Button, new() { Name = "Add open period" }).ClickAsync();
        await Assertions.Expect(page.Locator("tr", new() { HasTextString = name })).ToContainTextAsync("Open", new() { Timeout = 15_000 });
    }

    /// <summary>The maker requests funding for their own wallet; a second person approves and posts it.</summary>
    private async Task FundWalletAsync(string reference, int amount)
    {
        await using (var maker = await SignInAsync(Maker))
        {
            await maker.GotoAsync($"{AppFixture.UiBase}/petty-cash/fundings", new() { WaitUntil = WaitUntilState.NetworkIdle });
            await maker.GetByRole(AriaRole.Button, new() { Name = "New funding" }).ClickAsync();
            await maker.GetByLabel("Amount (UGX)").FillAsync(amount.ToString());
            await maker.GetByLabel("Source").FillAsync("Stanbic operating account");
            await maker.GetByLabel("Reference").FillAsync(reference);
            await maker.GetByRole(AriaRole.Button, new() { Name = "Submit for approval" }).ClickAsync();
            await Assertions.Expect(maker.Locator("tr", new() { HasTextString = reference })).ToContainTextAsync("Pending", new() { Timeout = 15_000 });
        }

        await using var approver = await SignInAsync(FundingApprover);
        await approver.GotoAsync($"{AppFixture.UiBase}/petty-cash/fundings", new() { WaitUntil = WaitUntilState.NetworkIdle });
        var row = approver.Locator("tr", new() { HasTextString = reference });
        await row.GetByLabel("Approve funding").ClickAsync();
        await approver.Locator(".mud-dialog").GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
        await Assertions.Expect(row).ToContainTextAsync("Approved", new() { Timeout = 15_000 });
    }

    /// <summary>Drafts an MTN request with evidence attached and submits it. Returns the request id.</summary>
    private async Task<int> RaiseRequestAsync(int amount, string phone, string purpose)
    {
        await using var page = await SignInAsync(Maker);
        await page.GotoAsync($"{AppFixture.UiBase}/petty-cash/requests/new", new() { WaitUntil = WaitUntilState.NetworkIdle });
        await ChooseAsync(page, "Category", "Break fast");
        await ChooseAsync(page, "Pay by", "MTN Mobile Money");
        await page.GetByLabel("Beneficiary phone").FillAsync(phone);
        await page.GetByLabel("Beneficiary name").FillAsync("E2E Beneficiary");
        await page.GetByLabel("Amount (UGX)").FillAsync(amount.ToString());
        await page.GetByLabel("What is it for").FillAsync(purpose);
        await page.GetByRole(AriaRole.Button, new() { Name = "Save draft" }).ClickAsync();
        await page.WaitForURLAsync(new Regex(@"/petty-cash/requests/\d+/edit$"), new() { Timeout = 30_000 });
        var id = int.Parse(Regex.Match(page.Url, @"requests/(\d+)/edit").Groups[1].Value);

        await page.Locator("input[type=file]").SetInputFilesAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "import-sample.pdf"));
        await Assertions.Expect(page.Locator(".mud-list-item", new() { HasTextString = "import-sample.pdf" }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Submit for approval" }).ClickAsync();
        await page.WaitForURLAsync($"**/petty-cash/requests/{id}", new() { Timeout = 30_000 });
        await Assertions.Expect(page.GetByText("awaiting approval at level 1")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        return id;
    }

    private async Task ApproveRequestAsync(int requestId)
    {
        await using var page = await SignInAsync(RequestApprover);
        await page.GotoAsync($"{AppFixture.UiBase}/petty-cash/requests/{requestId}", new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.GetByLabel("Comment (required to return or reject)").FillAsync("Approved by the browser test");
        await page.GetByRole(AriaRole.Button, new() { Name = "Approve level 1" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Approved by the browser test")).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    /// <summary>
    /// The worker books the request and publishes payment.requested; the payment service sends it to
    /// the MTN sandbox and polls until the outcome is known.
    /// </summary>
    private async Task WaitForPaymentAsync(int requestId)
    {
        await using var page = await SignInAsync(Maker);
        var url = $"{AppFixture.UiBase}/petty-cash/requests/{requestId}";
        var deadline = DateTime.UtcNow + PaymentWait;
        while (true)
        {
            await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle });
            var paid = page.Locator(".mud-alert", new() { HasTextString = "Paid " });
            var failed = page.Locator(".mud-alert", new() { HasTextRegex = new Regex("(Payment|Accounting) failed") });
            await page.GetByText("Amount").First.WaitForAsync(new() { Timeout = 30_000 });

            if (await paid.CountAsync() > 0)
            {
                await Assertions.Expect(paid).ToContainTextAsync("provider reference");
                return;
            }
            if (await failed.CountAsync() > 0)
                Assert.Fail($"Request {requestId}: {await failed.First.InnerTextAsync()}");
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Request {requestId} not paid after {PaymentWait.TotalMinutes} minutes: " +
                            await page.Locator("h1 + p").First.InnerTextAsync());
            await Task.Delay(10_000);
        }
    }

    private Task<IPage> SignInAsync(string user) => app.SignedInPageAsync(user, Password);

    // MudSelect and MudDatePicker render id="false" on the input, so GetByLabel cannot resolve them.
    private static ILocator ControlByLabel(IPage page, string label) =>
        page.Locator($"div.mud-input-control:has(label:text-is('{label}'))");

    private static async Task ChooseAsync(IPage page, string label, string option)
    {
        await ControlByLabel(page, label).ClickAsync();
        await page.Locator("div.mud-popover-open .mud-list-item", new() { HasTextString = option }).First.ClickAsync();
    }

    private static async Task PickDayAsync(IPage page, string label, int day)
    {
        await ControlByLabel(page, label).ClickAsync();
        await page.Locator("div.mud-popover-open button.mud-picker-calendar-day:not(.mud-hidden)",
            new() { HasTextRegex = new Regex($"^{day}$") }).First.ClickAsync();
    }

    private static async Task ExpectSnackbarAsync(IPage page) =>
        await page.Locator("div.mud-snackbar").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });

    // Enums may serialise as names or numbers depending on the endpoint.
    private static string StageName(JsonElement rule)
    {
        var stage = rule.GetProperty("stage");
        return stage.ValueKind == JsonValueKind.String ? stage.GetString()! : ((Stage)stage.GetInt32()).ToString();
    }

    private enum Stage { Funding = 1, Approval = 2, Settlement = 3 }

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
