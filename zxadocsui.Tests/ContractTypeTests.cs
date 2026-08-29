using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace zxadocsui.Tests;

// Contract types in a real browser (ZD-115): the catalogue, the field editor, the soft delete.
// Skips unless the signed-in account holds CreateContractTypes, which is granted per org.
[Collection("app")]
[Trait("Category", "Ui")]
public class ContractTypeTests(AppFixture app)
{
    private const string Password = "1234..34";
    private const string AdminUser = "lkatongole";

    private void SkipIfAppDown() =>
        Assert.SkipWhen(!app.AppReachable, app.SkipReason ?? "app not running");

    private static async Task SkipUnlessPermittedAsync()
    {
        using var http = await ApiAsync();
        using var body = JsonDocument.Parse(await http.GetStringAsync("/api/me/permissions"));
        var held = body.RootElement.GetProperty("data").EnumerateArray()
            .Select(p => p.GetProperty("name").GetString()).ToList();
        Assert.SkipWhen(!held.Contains("CreateContractTypes"),
            $"{AdminUser} does not hold CreateContractTypes; grant it on their role to run these.");
    }

    [Fact]
    public async Task The_catalogue_lists_the_organisations_contract_types()
    {
        SkipIfAppDown();
        await SkipUnlessPermittedAsync();
        var name = await SeedTypeAsync();

        try
        {
            await using var page = await app.SignedInPageAsync(AdminUser, Password);
            await page.GotoAsync($"{AppFixture.UiBase}/contract-types",
                new() { WaitUntil = WaitUntilState.NetworkIdle });

            await Assertions.Expect(page.GetByText(name)).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "New contract type" }))
                .ToBeVisibleAsync(new() { Timeout = 15_000 });
        }
        finally
        {
            await DeleteByNameAsync(name);
        }
    }

    [Fact]
    public async Task A_contract_type_is_created_with_its_fields()
    {
        SkipIfAppDown();
        await SkipUnlessPermittedAsync();
        var name = $"UI Probe {Guid.NewGuid():N}"[..24];

        try
        {
            await using var page = await app.SignedInPageAsync(AdminUser, Password);
            await page.GotoAsync($"{AppFixture.UiBase}/contract-types/new",
                new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.GetByLabel("Name").WaitForAsync(new() { Timeout = 30_000 });

            await page.GetByLabel("Name").FillAsync(name);
            await page.GetByLabel("Description").FillAsync("Created by a browser test");

            await page.GetByRole(AriaRole.Button, new() { Name = "Add field" }).ClickAsync();
            await FillFieldRowAsync(page, 0, "client_name", "Client name");

            await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
            await page.WaitForURLAsync(url => url.EndsWith("/contract-types"), new() { Timeout = 30_000 });

            // Persisted, not just optimistic UI: read it back through the API.
            var saved = await FetchByNameAsync(name);
            Assert.NotNull(saved);
            var field = Assert.Single(saved!.Value.GetProperty("fields").EnumerateArray());
            Assert.Equal("client_name", field.GetProperty("key").GetString());
            Assert.Equal("Client name", field.GetProperty("label").GetString());
        }
        finally
        {
            await DeleteByNameAsync(name);
        }
    }

    [Fact]
    public async Task Opening_a_type_shows_the_fields_it_already_has()
    {
        SkipIfAppDown();
        await SkipUnlessPermittedAsync();
        var name = await SeedTypeAsync(fieldKey: "contract_value");

        try
        {
            var id = (await FetchByNameAsync(name))!.Value.GetProperty("id").GetInt32();
            await using var page = await app.SignedInPageAsync(AdminUser, Password);
            await page.GotoAsync($"{AppFixture.UiBase}/contract-types/{id}",
                new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.GetByLabel("Name").WaitForAsync(new() { Timeout = 30_000 });

            // Empty here means an edit would save the type with its fields stripped.
            await Assertions.Expect(page.GetByLabel("Name")).ToHaveValueAsync(name, new() { Timeout = 15_000 });
            await Assertions.Expect(FieldKeyInput(page, 0))
                .ToHaveValueAsync("contract_value", new() { Timeout = 15_000 });
        }
        finally
        {
            await DeleteByNameAsync(name);
        }
    }

    [Fact]
    public async Task An_edit_replaces_the_field_set()
    {
        SkipIfAppDown();
        await SkipUnlessPermittedAsync();
        var name = await SeedTypeAsync(fieldKey: "old_key");

        try
        {
            var id = (await FetchByNameAsync(name))!.Value.GetProperty("id").GetInt32();
            await using var page = await app.SignedInPageAsync(AdminUser, Password);
            await page.GotoAsync($"{AppFixture.UiBase}/contract-types/{id}",
                new() { WaitUntil = WaitUntilState.NetworkIdle });
            await FieldKeyInput(page, 0).WaitForAsync(new() { Timeout = 30_000 });

            await FieldKeyInput(page, 0).FillAsync("new_key");
            await page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
            await page.WaitForURLAsync(url => url.EndsWith("/contract-types"), new() { Timeout = 30_000 });

            var saved = await FetchByNameAsync(name);
            var field = Assert.Single(saved!.Value.GetProperty("fields").EnumerateArray());
            Assert.Equal("new_key", field.GetProperty("key").GetString());
        }
        finally
        {
            await DeleteByNameAsync(name);
        }
    }

    [Fact]
    public async Task Deleting_a_type_removes_it_from_the_list_but_not_the_database()
    {
        SkipIfAppDown();
        await SkipUnlessPermittedAsync();
        var name = await SeedTypeAsync();
        var id = (await FetchByNameAsync(name))!.Value.GetProperty("id").GetInt32();

        await using var page = await app.SignedInPageAsync(AdminUser, Password);
        await page.GotoAsync($"{AppFixture.UiBase}/contract-types",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.GetByText(name).WaitForAsync(new() { Timeout = 30_000 });

        await page.GetByRole(AriaRole.Row, new() { Name = name })
            .GetByRole(AriaRole.Button, new() { Name = "Delete contract type" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Delete" }).Last.ClickAsync();

        await Assertions.Expect(page.GetByText(name)).ToHaveCountAsync(0, new() { Timeout = 30_000 });

        // Soft, not hard: still there, just inactive, so drafted contracts keep resolving.
        using var http = await ApiAsync();
        using var body = JsonDocument.Parse(await http.GetStringAsync($"/api/contract-types/{id}"));
        Assert.False(body.RootElement.GetProperty("data").GetProperty("isActive").GetBoolean());
    }

    // ---------------------------------------------------------------- helpers

    // MudBlazor renders each cell's input without a label, so the rows are addressed positionally.
    private static ILocator FieldKeyInput(IPage page, int row) =>
        page.Locator("tbody tr input[type='text']").Nth(row * 2);

    private static ILocator FieldLabelInput(IPage page, int row) =>
        page.Locator("tbody tr input[type='text']").Nth(row * 2 + 1);

    private static async Task FillFieldRowAsync(IPage page, int row, string key, string label)
    {
        await FieldKeyInput(page, row).WaitForAsync(new() { Timeout = 15_000 });
        await FieldKeyInput(page, row).FillAsync(key);
        await FieldLabelInput(page, row).FillAsync(label);
    }

    /// <summary>Creates a type straight through the API, so a UI test starts from a known state.</summary>
    private static async Task<string> SeedTypeAsync(string fieldKey = "client_name")
    {
        var name = $"Seeded {Guid.NewGuid():N}"[..20];
        using var http = await ApiAsync();
        var payload = JsonSerializer.Serialize(new
        {
            name,
            description = "Seeded by a browser test",
            fields = new[] { new { key = fieldKey, label = fieldKey, fieldType = 0, isRequired = true } },
        });
        var response = await http.PostAsync("/api/contract-types",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        return name;
    }

    private static async Task<JsonElement?> FetchByNameAsync(string name)
    {
        using var http = await ApiAsync();
        using var body = JsonDocument.Parse(await http.GetStringAsync("/api/contract-types"));
        foreach (var type in body.RootElement.GetProperty("data").EnumerateArray())
        {
            if (type.GetProperty("name").GetString() == name) return type.Clone();
        }
        return null;
    }

    /// <summary>Best effort, so a run leaves the catalogue as it found it.</summary>
    private static async Task DeleteByNameAsync(string name)
    {
        try
        {
            var type = await FetchByNameAsync(name);
            if (type is null) return;
            using var http = await ApiAsync();
            await http.DeleteAsync($"/api/contract-types/{type.Value.GetProperty("id").GetInt32()}");
        }
        catch
        {
            // A cleanup failure must not mask the assertion that already ran.
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
}
