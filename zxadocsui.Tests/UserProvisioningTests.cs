using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace zxadocsui.Tests;

// The user-provisioning flow in a real browser (ZD-105): creating a user with no password, the
// invitation confirmation, and the pending state and resend action in the list.
//
// Runs as an account holding CreateUser/ManageUsers, since the dev-default login holds neither and
// AddUser redirects it away. Every user this suite creates is deleted through the API afterwards.
[Collection("app")]
[Trait("Category", "Ui")]
public class UserProvisioningTests(AppFixture app)
{
    private const string Password = "1234..34";

    // Support Admin: the only seeded role holding CreateUser, which AddUser gates on.
    private const string AdminUser = "lkatongole";

    private static readonly string RunTag = Guid.NewGuid().ToString("N")[..6];

    private void SkipIfAppDown() =>
        Assert.SkipWhen(!app.AppReachable, app.SkipReason ?? "app not running");

    // ---------------------------------------------------------------- create

    [Fact]
    public async Task The_create_form_offers_no_password_field()
    {
        SkipIfAppDown();
        await using var page = await app.SignedInPageAsync(AdminUser, Password);

        await page.GotoAsync($"{AppFixture.UiBase}/users/add", new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.GetByLabel("Full Name").WaitForAsync(new() { Timeout = 30_000 });

        // The server generates the password now; an admin must not be able to choose one.
        Assert.Equal(0, await page.Locator("input[type='password']").CountAsync());
    }

    [Fact]
    public async Task The_roles_dropdown_offers_the_organisations_roles()
    {
        SkipIfAppDown();
        await using var page = await app.SignedInPageAsync(AdminUser, Password);

        await page.GotoAsync($"{AppFixture.UiBase}/users/add", new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.GetByLabel("Roles").ClickAsync(new() { Timeout = 30_000 });

        // Empty here means the form can never satisfy its own "select at least one role" rule.
        var options = page.Locator("div.mud-list-item");
        await options.First.WaitForAsync(new() { Timeout = 15_000 });
        Assert.True(await options.CountAsync() > 0, "the roles dropdown rendered no options");
    }

    [Fact]
    public async Task Creating_a_user_reports_where_the_invitation_was_sent()
    {
        SkipIfAppDown();
        var username = $"zdui{RunTag}a";
        await using var page = await app.SignedInPageAsync(AdminUser, Password);

        try
        {
            await CreateUserAsync(page, username);

            // The admin never sees the password, so the address is the fact worth confirming.
            await VisibleAsync(page.GetByText($"{username}@example.test"), 20_000);
            await page.WaitForURLAsync("**/users", new() { Timeout = 30_000 });
        }
        finally
        {
            await DeleteUserAsync(username);
        }
    }

    // ---------------------------------------------------------------- list

    [Fact]
    public async Task A_new_user_is_listed_as_never_signed_in_and_can_be_re_invited()
    {
        SkipIfAppDown();
        var username = $"zdui{RunTag}b";
        await using var page = await app.SignedInPageAsync(AdminUser, Password);

        try
        {
            await CreateUserAsync(page, username);

            var row = await RowForAsync(page, username);
            await VisibleAsync(row.GetByText("Never signed in"), 20_000);

            var resend = row.GetByRole(AriaRole.Button, new() { Name = "Resend invite" });
            await VisibleAsync(resend, 10_000);

            await resend.ClickAsync();
            await VisibleAsync(page.GetByText("A new invitation has been emailed"), 20_000);
        }
        finally
        {
            await DeleteUserAsync(username);
        }
    }

    [Fact]
    public async Task An_activated_user_offers_no_resend_action()
    {
        SkipIfAppDown();
        await using var page = await app.SignedInPageAsync(AdminUser, Password);

        await page.GotoAsync($"{AppFixture.UiBase}/users", new() { WaitUntil = WaitUntilState.NetworkIdle });

        // The admin signed in with its own password, so it has activated by definition.
        var row = await RowForAsync(page, AdminUser);
        await VisibleAsync(row.GetByText("Active"), 20_000);
        Assert.Equal(0, await row.GetByRole(AriaRole.Button, new() { Name = "Resend invite" }).CountAsync());
    }

    // ---------------------------------------------------------------- login

    [Fact]
    public async Task An_existing_user_can_still_sign_in_after_the_passwords_were_hashed()
    {
        SkipIfAppDown();

        // ZD-98 rewrote every stored password in place. Reaching the dashboard is the proof that
        // the migration did not lock anyone out.
        await using var page = await app.SignedInPageAsync(AdminUser, Password);

        Assert.Contains("/dashboard", page.Url);
    }

    [Fact]
    public async Task A_wrong_password_does_not_sign_in()
    {
        SkipIfAppDown();
        var context = await app.Browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true,
            ViewportSize = new() { Width = 1600, Height = 1200 },
        });
        await using var _ = context;
        var page = await context.NewPageAsync();

        await page.GotoAsync(AppFixture.UiBase, new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.Locator("input[type='text']").First.FillAsync(AdminUser);
        var password = page.Locator("input[type='password']").First;
        await password.FillAsync("definitely-not-the-password");

        // Validation has to run before the button enables; ZD-107 removed the prefilled defaults
        // that used to make the form valid on arrival.
        await password.PressAsync("Enter");

        var login = page.GetByRole(AriaRole.Button, new() { Name = "Login" });
        await Assertions.Expect(login).ToBeEnabledAsync(new() { Timeout = 15_000 });
        await login.ClickAsync(new() { Timeout = 15_000 });

        await page.WaitForTimeoutAsync(4000);
        Assert.DoesNotContain("/dashboard", page.Url);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Fills and submits the create-user form. No password is typed anywhere.</summary>
    private static async Task CreateUserAsync(IPage page, string username)
    {
        await page.GotoAsync($"{AppFixture.UiBase}/users/add", new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.GetByLabel("Full Name").WaitForAsync(new() { Timeout = 30_000 });

        await page.GetByLabel("Full Name").FillAsync($"UI Probe {username}");
        await page.GetByLabel("Username").FillAsync(username);
        await page.GetByLabel("Email").FillAsync($"{username}@example.test");
        await page.GetByLabel("Department").FillAsync("1");
        await page.GetByLabel("Grade").FillAsync("G1");
        await page.GetByLabel("Country Code").FillAsync("256");
        await page.GetByLabel("Phone Number").FillAsync("700000123");

        await page.GetByLabel("Roles").ClickAsync();
        var option = page.Locator("div.mud-list-item").First;
        await option.WaitForAsync(new() { Timeout = 15_000 });
        await option.ClickAsync();

        await CloseAnyPopoverAsync(page);

        // The validator requires a signature, and MudFileUpload wraps a real file input.
        await page.Locator("input[type='file']").First.SetInputFilesAsync(new FilePayload
        {
            Name = "signature.png",
            MimeType = "image/png",
            Buffer = OnePixelPng(),
        });

        // The file input re-opens nothing, but MudBlazor may still have an overlay mounted.
        await CloseAnyPopoverAsync(page);

        var submit = page.GetByRole(AriaRole.Button, new() { Name = "Create User" });
        try
        {
            await submit.ClickAsync(new() { Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            // A lingering MudBlazor overlay swallows real mouse events; dispatching straight at
            // the button still runs Blazor's handler, which is what this test is here to exercise.
            await submit.DispatchEventAsync("click");
        }
    }

    /// <summary>
    /// MultiSelection leaves the popover open over the rest of the form, and Escape does not close
    /// it. Clicking MudBlazor's own overlay is what dismisses it.
    /// </summary>
    private static async Task CloseAnyPopoverAsync(IPage page)
    {
        var overlay = page.Locator("div.mud-overlay");
        if (await overlay.CountAsync() > 0)
        {
            await overlay.First.ClickAsync(new() { Force = true, Timeout = 5_000 });
            await page.WaitForTimeoutAsync(400);
        }
    }

    /// <summary>The data-grid row containing a username.</summary>
    private static async Task<ILocator> RowForAsync(IPage page, string username)
    {
        if (!page.Url.Contains("/users"))
            await page.GotoAsync($"{AppFixture.UiBase}/users", new() { WaitUntil = WaitUntilState.NetworkIdle });

        var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = username });
        await row.First.WaitForAsync(new() { Timeout = 30_000 });
        return row.First;
    }

    private static async Task VisibleAsync(ILocator locator, int timeout) =>
        await locator.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeout });

    private static byte[] OnePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>
    /// Removes a user created by this suite, through the API so the run leaves no rows behind.
    /// Failures are swallowed: a cleanup problem must not mask the assertion that already ran.
    /// </summary>
    private static async Task DeleteUserAsync(string username)
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            };
            using var http = new HttpClient(handler) { BaseAddress = new Uri(AppFixture.ApiBase) };

            var login = await http.PostAsync("/api/user/login", Json(new { username = AdminUser, password = Password }));
            if (!login.IsSuccessStatusCode) return;

            using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            var token = body.RootElement.GetProperty("data").GetProperty("token").GetString();
            if (string.IsNullOrEmpty(token)) return;

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var found = await http.GetAsync($"/api/users/{username}");
            if (!found.IsSuccessStatusCode) return;

            using var user = JsonDocument.Parse(await found.Content.ReadAsStringAsync());
            var id = user.RootElement.GetProperty("data").GetProperty("id").GetInt32();
            await http.DeleteAsync($"/api/users/{id}");
        }
        catch
        {
            // Best effort only.
        }
    }

    private static StringContent Json(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
}
