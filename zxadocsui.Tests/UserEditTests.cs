using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace zxadocsui.Tests;

// Editing a user in a real browser (ZD-114): the row action, the populated form, the signature
// preview, and saving through PATCH rather than a second create.
//
// Runs as an account holding ManageUsers, which both the page and the update endpoint require.
[Collection("app")]
[Trait("Category", "Ui")]
public class UserEditTests(AppFixture app)
{
    private const string Password = "1234..34";
    private const string AdminUser = "lkatongole";

    private void SkipIfAppDown() =>
        Assert.SkipWhen(!app.AppReachable, app.SkipReason ?? "app not running");

    [Fact]
    public async Task Every_row_offers_an_edit_action()
    {
        SkipIfAppDown();
        await using var page = await app.SignedInPageAsync(AdminUser, Password);

        await page.GotoAsync($"{AppFixture.UiBase}/users", new() { WaitUntil = WaitUntilState.NetworkIdle });
        var edit = page.GetByRole(AriaRole.Link, new() { Name = "Edit" });
        await edit.First.WaitForAsync(new() { Timeout = 30_000 });

        Assert.True(await edit.CountAsync() > 0, "no row offered an Edit action");
    }

    [Fact]
    public async Task The_edit_action_routes_to_the_users_reference()
    {
        SkipIfAppDown();
        await using var page = await app.SignedInPageAsync(AdminUser, Password);

        await page.GotoAsync($"{AppFixture.UiBase}/users", new() { WaitUntil = WaitUntilState.NetworkIdle });
        var edit = page.GetByRole(AriaRole.Link, new() { Name = "Edit" }).First;
        await edit.WaitForAsync(new() { Timeout = 30_000 });

        var href = await edit.GetAttributeAsync("href") ?? string.Empty;

        // A reference, not the numeric id: the signature endpoints are keyed on it.
        Assert.StartsWith("/users/edit/", href);
        Assert.True(Guid.TryParse(href["/users/edit/".Length..], out var reference) && reference != Guid.Empty,
            $"edit link carried no usable reference: '{href}'");
    }

    [Fact]
    public async Task The_form_is_populated_with_the_users_details()
    {
        SkipIfAppDown();
        var (reference, username, name) = await AnyUserAsync();
        await using var page = await app.SignedInPageAsync(AdminUser, Password);

        await page.GotoAsync($"{AppFixture.UiBase}/users/edit/{reference}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.GetByLabel("Full Name").WaitForAsync(new() { Timeout = 30_000 });

        await Assertions.Expect(page.GetByLabel("Full Name")).ToHaveValueAsync(name, new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByLabel("Username")).ToHaveValueAsync(username);
    }

    [Fact]
    public async Task The_page_announces_itself_as_an_edit()
    {
        SkipIfAppDown();
        var (reference, _, _) = await AnyUserAsync();
        await using var page = await app.SignedInPageAsync(AdminUser, Password);

        await page.GotoAsync($"{AppFixture.UiBase}/users/edit/{reference}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        // Otherwise an operator cannot tell an edit from a create, and expects an invitation.
        await Assertions.Expect(page.GetByText("Edit User").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Save Changes" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task The_username_cannot_be_changed()
    {
        SkipIfAppDown();
        var (reference, _, _) = await AnyUserAsync();
        await using var page = await app.SignedInPageAsync(AdminUser, Password);

        await page.GotoAsync($"{AppFixture.UiBase}/users/edit/{reference}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.GetByLabel("Username").WaitForAsync(new() { Timeout = 30_000 });

        // It identifies the account at sign-in and in the audit trail.
        Assert.True(await page.GetByLabel("Username").IsDisabledAsync());
    }

    [Fact]
    public async Task The_users_roles_come_back_selected()
    {
        SkipIfAppDown();
        var reference = await UserWithARoleAsync();
        await using var page = await app.SignedInPageAsync(AdminUser, Password);

        await page.GotoAsync($"{AppFixture.UiBase}/users/edit/{reference}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.GetByLabel("Roles").WaitForAsync(new() { Timeout = 30_000 });

        // Empty here means saving an untouched form strips every role the user holds.
        await Assertions.Expect(page.GetByLabel("Roles")).Not.ToHaveValueAsync("", new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task The_stored_signature_is_previewed()
    {
        SkipIfAppDown();
        var reference = await UserWithASignatureAsync();
        Assert.SkipWhen(reference is null, "no user in this organisation has a signature on file");

        await using var page = await app.SignedInPageAsync(AdminUser, Password);
        await page.GotoAsync($"{AppFixture.UiBase}/users/edit/{reference}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        var preview = page.GetByAltText("Signature");
        await preview.WaitForAsync(new() { Timeout = 30_000 });

        // A data URI, because /api/users/signature needs a bearer token an <img> would not send.
        var src = await preview.GetAttributeAsync("src") ?? string.Empty;
        Assert.StartsWith("data:image/", src);

        // naturalWidth 0 means the browser could not decode it, however good the markup looks.
        var width = await preview.EvaluateAsync<int>("img => img.naturalWidth");
        Assert.True(width > 0, "the signature preview rendered but decoded to nothing");
    }

    [Fact]
    public async Task A_new_signature_previews_before_it_is_saved()
    {
        SkipIfAppDown();
        var (reference, _, _) = await AnyUserAsync();
        await using var page = await app.SignedInPageAsync(AdminUser, Password);

        await page.GotoAsync($"{AppFixture.UiBase}/users/edit/{reference}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.GetByLabel("Full Name").WaitForAsync(new() { Timeout = 30_000 });

        await page.Locator("input[type='file']").First.SetInputFilesAsync(new FilePayload
        {
            Name = "replacement.png",
            MimeType = "image/png",
            Buffer = OnePixelPng(),
        });

        var preview = page.GetByAltText("Signature");
        await preview.WaitForAsync(new() { Timeout = 15_000 });
        Assert.StartsWith("data:image/", await preview.GetAttributeAsync("src") ?? string.Empty);
    }

    [Fact]
    public async Task Dropping_an_image_on_the_zone_previews_it_in_place()
    {
        SkipIfAppDown();
        var (reference, _, _) = await AnyUserAsync();
        await using var page = await app.SignedInPageAsync(AdminUser, Password);

        await page.GotoAsync($"{AppFixture.UiBase}/users/edit/{reference}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        var zone = page.Locator(".signature-drop");
        await zone.WaitForAsync(new() { Timeout = 30_000 });
        var input = page.Locator("input[type='file']").First;

        // The file input has to cover the zone for a drop anywhere on it to land on the input
        // rather than on the page, which would make the browser navigate to the dropped file.
        var zoneBox = await zone.BoundingBoxAsync();
        var inputBox = await input.BoundingBoxAsync();
        Assert.NotNull(zoneBox);
        Assert.NotNull(inputBox);
        Assert.True(Math.Abs(inputBox!.Width - zoneBox!.Width) < 2 &&
                    Math.Abs(inputBox.Height - zoneBox.Height) < 2 &&
                    Math.Abs(inputBox.X - zoneBox.X) < 2 && Math.Abs(inputBox.Y - zoneBox.Y) < 2,
            $"the input does not cover the drop zone: input {inputBox.Width}x{inputBox.Height} "
            + $"at ({inputBox.X},{inputBox.Y}), zone {zoneBox.Width}x{zoneBox.Height} "
            + $"at ({zoneBox.X},{zoneBox.Y})");

        // A synthesised drop cannot run the browser's default action, which is what puts the file
        // on the input. Assigning it and raising change is exactly what that default action does.
        await input.EvaluateAsync(
            @"(input, base64) => {
                const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
                const transfer = new DataTransfer();
                transfer.items.add(new File([bytes], 'dropped.png', { type: 'image/png' }));
                input.files = transfer.files;
                input.dispatchEvent(new Event('change', { bubbles: true }));
            }", Convert.ToBase64String(OnePixelPng()));

        // Wait on the filename, not on the image: a user who already has a signature is showing a
        // preview before the drop, so waiting on an img alone passes without the drop registering.
        await Assertions.Expect(zone).ToContainTextAsync("dropped.png", new() { Timeout = 15_000 });

        // In place: the preview belongs to the drop zone, not to a panel beside it.
        var preview = zone.GetByAltText("Signature");
        Assert.StartsWith("data:image/", await preview.GetAttributeAsync("src") ?? string.Empty);
    }

    [Fact]
    public async Task An_unknown_reference_sends_the_operator_back_to_the_list()
    {
        SkipIfAppDown();
        await using var page = await app.SignedInPageAsync(AdminUser, Password);

        await page.GotoAsync($"{AppFixture.UiBase}/users/edit/{Guid.NewGuid()}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        await page.WaitForURLAsync(url => url.Contains("/users") && !url.Contains("/edit/"),
            new() { Timeout = 30_000 });
        Assert.DoesNotContain("/edit/", page.Url);
    }

    [Fact]
    public async Task Saving_an_edit_updates_the_user_and_returns_to_the_list()
    {
        SkipIfAppDown();
        var (reference, username, originalName) = await AnyUserAsync();
        var original = await FetchUserAsync(reference);
        var edited = $"{originalName} (edited)";

        await using var page = await app.SignedInPageAsync(AdminUser, Password);
        try
        {
            await page.GotoAsync($"{AppFixture.UiBase}/users/edit/{reference}",
                new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.GetByLabel("Full Name").WaitForAsync(new() { Timeout = 30_000 });

            await page.GetByLabel("Full Name").FillAsync(edited);
            await CompleteRequiredFieldsAsync(page);
            await page.GetByRole(AriaRole.Button, new() { Name = "Save Changes" }).ClickAsync(new() { Timeout = 15_000 });

            // Back on the list, and the new name is in the table.
            await page.WaitForURLAsync(url => url.EndsWith("/users"), new() { Timeout = 30_000 });
            await Assertions.Expect(page.GetByText(edited).First).ToBeVisibleAsync(new() { Timeout = 30_000 });

            // And it really persisted, rather than only being echoed by the grid.
            var reloaded = await FetchUserAsync(reference);
            Assert.Equal(edited, reloaded.GetProperty("name").GetString());
            Assert.Equal(username, reloaded.GetProperty("username").GetString());
        }
        finally
        {
            await RestoreUserAsync(original);
        }
    }

    [Fact]
    public async Task Saving_without_touching_the_signature_keeps_the_stored_one()
    {
        SkipIfAppDown();
        var reference = await UserWithASignatureAsync();
        Assert.SkipWhen(reference is null, "no user in this organisation has a signature on file");

        var original = await FetchUserAsync(reference!.Value);
        var before = original.GetProperty("signatureId").GetString();

        await using var page = await app.SignedInPageAsync(AdminUser, Password);
        try
        {
            await page.GotoAsync($"{AppFixture.UiBase}/users/edit/{reference}",
                new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.GetByLabel("Full Name").WaitForAsync(new() { Timeout = 30_000 });

            await page.GetByLabel("Grade").FillAsync("G2");
            await CompleteRequiredFieldsAsync(page);
            await page.GetByRole(AriaRole.Button, new() { Name = "Save Changes" }).ClickAsync(new() { Timeout = 15_000 });
            await page.WaitForURLAsync(url => url.EndsWith("/users"), new() { Timeout = 30_000 });

            // The stored path must be untouched: an edit that silently wiped a signature would
            // break every document the user has yet to sign.
            var after = (await FetchUserAsync(reference.Value)).GetProperty("signatureId").GetString();
            Assert.Equal(before, after);
        }
        finally
        {
            await RestoreUserAsync(original);
        }
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<(Guid Reference, string Username, string Name)> AnyUserAsync()
    {
        var users = await ListUsersAsync();
        var user = users.First(u => u.GetProperty("username").GetString() != AdminUser);
        return (user.GetProperty("userReference").GetGuid(),
                user.GetProperty("username").GetString()!,
                user.GetProperty("name").GetString()!);
    }

    private static async Task<Guid> UserWithARoleAsync()
    {
        var users = await ListUsersAsync();
        var user = users.First(u => u.GetProperty("roles").GetArrayLength() > 0);
        return user.GetProperty("userReference").GetGuid();
    }

    private static async Task<Guid?> UserWithASignatureAsync()
    {
        var users = await ListUsersAsync();
        foreach (var user in users)
        {
            if (!string.IsNullOrWhiteSpace(user.GetProperty("signatureId").GetString()))
                return user.GetProperty("userReference").GetGuid();
        }
        return null;
    }

    private static async Task<List<JsonElement>> ListUsersAsync()
    {
        using var http = await ApiAsync();
        using var body = JsonDocument.Parse(await http.GetStringAsync("/api/users"));
        return body.RootElement.GetProperty("data").EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static async Task<JsonElement> FetchUserAsync(Guid reference)
    {
        using var http = await ApiAsync();
        using var body = JsonDocument.Parse(await http.GetStringAsync($"/api/users/reference/{reference}"));
        return body.RootElement.GetProperty("data").Clone();
    }

    /// <summary>
    /// Some seeded users have no department or country code, which the form requires. An operator
    /// editing them would have to fill them in, so the test does the same.
    /// </summary>
    private static async Task CompleteRequiredFieldsAsync(IPage page)
    {
        await ChooseDepartmentAsync(page);
        await FillIfEmptyAsync(page.GetByLabel("Grade"), "G1");
        await FillIfEmptyAsync(page.GetByLabel("Country Code"), "256");
        await FillIfEmptyAsync(page.GetByLabel("Phone Number"), "700000123");
    }

    private static async Task FillIfEmptyAsync(ILocator field, string value)
    {
        var current = await field.InputValueAsync();
        if (string.IsNullOrWhiteSpace(current) || current == "0")
            await field.FillAsync(value);
    }

    /// <summary>Puts the whole record back, so a run leaves the seeded users as it found them.</summary>
    private static async Task RestoreUserAsync(JsonElement original)
    {
        try
        {
            using var http = await ApiAsync();
            await http.PatchAsync($"/api/users/{original.GetProperty("id").GetInt32()}",
                new StringContent(original.GetRawText(), Encoding.UTF8, "application/json"));
        }
        catch
        {
            // Best effort: a cleanup failure must not mask the assertion that already ran.
        }
    }

    private static async Task<HttpClient> ApiAsync()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri(AppFixture.ApiBase) };

        var login = await http.PostAsync("/api/user/login",
            new StringContent(JsonSerializer.Serialize(new { username = AdminUser, password = Password }),
                Encoding.UTF8, "application/json"));

        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = body.RootElement.GetProperty("data").GetProperty("token").GetString();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static byte[] OnePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>
    /// Department became a picker in ZD-132: it was a free-text box, but the API requires the value
    /// to parse as a department id, so typing a name always came back 400.
    ///
    /// Located by its control rather than GetByLabel: a MudSelect renders a hidden input alongside
    /// the visible one, and .First resolves to the hidden one - which is never clickable and never
    /// readable with InputValueAsync. Waits for the popover to detach, or the overlay swallows the
    /// click on Save that follows.
    /// </summary>
    private static async Task ChooseDepartmentAsync(IPage page)
    {
        var control = page.Locator("div.mud-input-control:has(label:text-is('Department'))");
        if (await control.CountAsync() == 0) return;

        await control.First.ClickAsync(new() { Timeout = 15_000 });
        var options = page.Locator("div.mud-popover-open .mud-list-item");
        try
        {
            await options.First.WaitForAsync(new() { Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            await page.Keyboard.PressAsync("Escape");
            return;
        }

        await options.First.ClickAsync();
        await page.Locator("div.mud-popover-open").First
            .WaitForAsync(new() { State = WaitForSelectorState.Detached, Timeout = 10_000 });
    }
}
