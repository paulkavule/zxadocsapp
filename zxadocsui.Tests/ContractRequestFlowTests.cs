using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace zxadocsui.Tests;

// The contract initiation flow in a real browser: raise a request, preview a template with its
// values merged, confirm, and land on a drafting form already filled in.
//
// Two actors, as the permission model requires: sgeorge holds InitiateContract and raises the
// request; stamale holds CreateDraft and drafts it. Neither can do the other's half.
[Collection("app")]
[Trait("Category", "Ui")]
public class ContractRequestFlowTests(AppFixture app)
{
    private const string Password = "1234..34";
    private const string Initiator = "sgeorge";   // ViewContracts + InitiateContract
    private const string Drafter = "stamale";     // ViewContracts + CreateDraft

    private void SkipIfAppDown() =>
        Assert.SkipWhen(!app.AppReachable, app.SkipReason ?? "app not running");

    [Fact]
    public async Task A_request_appears_in_the_catalogue_with_its_term_derived()
    {
        SkipIfAppDown();
        var seeded = await SeedRequestAsync();

        try
        {
            await using var page = await app.SignedInPageAsync(Drafter, Password);
            await page.GotoAsync($"{AppFixture.UiBase}/contract-requests",
                new() { WaitUntil = WaitUntilState.NetworkIdle });

            var row = page.GetByRole(AriaRole.Row, new() { Name = seeded.Reference });
            await row.WaitForAsync(new() { Timeout = 30_000 });

            // Award and expiry are contract type fields, so a derived term proves they were
            // captured as values rather than typed as columns.
            await Assertions.Expect(row).ToContainTextAsync("2 years", new() { Timeout = 15_000 });
        }
        finally { await DeleteRequestAsync(seeded.Id); }
    }

    [Fact]
    public async Task Previewing_shows_the_contract_with_the_requests_values_merged()
    {
        SkipIfAppDown();
        var seeded = await SeedRequestAsync();

        try
        {
            await using var page = await app.SignedInPageAsync(Drafter, Password);
            await page.GotoAsync($"{AppFixture.UiBase}/contract-requests/{seeded.Id}/draft",
                new() { WaitUntil = WaitUntilState.NetworkIdle });

            // No click: the page selects the first template on arrival and previews it.
            // PagePreview renders the document into a sandboxed iframe, so the merged text lives
            // in that frame rather than the page.
            var document = page.FrameLocator("iframe.zx-frame");
            await Assertions.Expect(document.GetByText(seeded.Marker).First)
                .ToBeVisibleAsync(new() { Timeout = 30_000 });

            // Looking creates nothing: the request is still open, and now offers to be used.
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Use this template" }))
                .ToBeVisibleAsync(new() { Timeout = 15_000 });
            Assert.Equal("Open", await StatusOfAsync(seeded.Id));
        }
        finally { await DeleteRequestAsync(seeded.Id); }
    }

    // The preview holds a real letterhead. Stored image URLs are canonical and unsigned, and the
    // sandboxed iframe resolves those against the UI's origin, so an unsigned one 404s silently.
    [Fact]
    public async Task The_preview_renders_the_templates_images()
    {
        SkipIfAppDown();
        var seeded = await SeedRequestAsync();

        try
        {
            await using var page = await app.SignedInPageAsync(Drafter, Password);
            await page.GotoAsync($"{AppFixture.UiBase}/contract-requests/{seeded.Id}/draft",
                new() { WaitUntil = WaitUntilState.NetworkIdle });

            var document = page.FrameLocator("iframe.zx-frame");
            await Assertions.Expect(document.GetByText(seeded.Marker).First)
                .ToBeVisibleAsync(new() { Timeout = 30_000 });

            var images = document.Locator("img");
            var count = await images.CountAsync();
            Assert.SkipWhen(count == 0, "this contract type's template carries no image");

            // naturalWidth is the only honest proof: a broken <img> is still present and visible.
            for (var i = 0; i < count; i++)
            {
                var loaded = await images.Nth(i).EvaluateAsync<bool>(
                    "img => img.complete && img.naturalWidth > 0");
                var src = await images.Nth(i).GetAttributeAsync("src");
                Assert.True(loaded, $"the preview image did not load: {src}");
            }
        }
        finally { await DeleteRequestAsync(seeded.Id); }
    }

    [Fact]
    public async Task A_template_of_another_contract_type_is_never_offered()
    {
        SkipIfAppDown();
        var seeded = await SeedRequestAsync();

        try
        {
            await using var page = await app.SignedInPageAsync(Drafter, Password);
            await page.GotoAsync($"{AppFixture.UiBase}/contract-requests/{seeded.Id}/draft",
                new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.GetByRole(AriaRole.Button, new() { Name = "Use this template" })
                .WaitForAsync(new() { Timeout = 30_000 });

            var offered = await page.GetByRole(AriaRole.Button).AllInnerTextsAsync();
            var strangers = await TemplateNamesOfOtherTypesAsync(seeded.ContractTypeId);
            Assert.SkipWhen(strangers.Count == 0, "no template of another contract type exists");

            foreach (var name in strangers)
                Assert.DoesNotContain(offered, row => row.Contains(name, StringComparison.OrdinalIgnoreCase));
        }
        finally { await DeleteRequestAsync(seeded.Id); }
    }

    [Fact]
    public async Task Confirming_a_template_opens_a_drafting_form_already_filled_in()
    {
        SkipIfAppDown();
        // Not cleaned up: once drafted, a request can no longer be deleted, which is the rule
        // this flow establishes. Its title carries a probe tag so leftovers are identifiable.
        var seeded = await SeedRequestAsync();

        await using var page = await app.SignedInPageAsync(Drafter, Password);
        await page.GotoAsync($"{AppFixture.UiBase}/contract-requests/{seeded.Id}/draft",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        var confirm = page.GetByRole(AriaRole.Button, new() { Name = "Use this template" });
        await confirm.WaitForAsync(new() { Timeout = 30_000 });
        await confirm.ClickAsync();

        // The editor, not the read-only detail page: this is where the contract is written.
        await page.WaitForURLAsync(url => url.Contains("/drafts/") && url.EndsWith("/edit"),
            new() { Timeout = 30_000 });

        // The whole point of the flow: the drafting form carries the request's values, and says
        // they are not editable here.
        await Assertions.Expect(page.GetByText("These come from the contract request").First)
            .ToBeVisibleAsync(new() { Timeout = 30_000 });

        // Read the value PROPERTY, not the attribute: MudBlazor sets the former, so an
        // input[value='...'] selector never matches.
        await page.WaitForFunctionAsync(
            "marker => [...document.querySelectorAll('input')].some(i => i.value === marker)",
            seeded.Marker, new() { Timeout = 30_000 });

        // Read-only here: a wrong value is corrected on the request, not in the contract.
        var disabled = await page.EvaluateAsync<bool>(
            "marker => [...document.querySelectorAll('input')].find(i => i.value === marker).disabled",
            seeded.Marker);
        Assert.True(disabled, "the inherited value was editable in the draft");

        Assert.Equal("Drafted", await StatusOfAsync(seeded.Id));
    }

    // ---------------------------------------------------------------- helpers

    private sealed record Seeded(int Id, string Reference, string Marker, int ContractTypeId);

    /// <summary>Raises a request through the API as the initiator, so the UI test starts ready.</summary>
    private static async Task<Seeded> SeedRequestAsync()
    {
        using var http = await ApiAsync(Initiator);

        using var types = JsonDocument.Parse(await http.GetStringAsync("/api/contract-types"));
        var type = types.RootElement.GetProperty("data").EnumerateArray().First();
        var typeId = type.GetProperty("id").GetInt32();

        // A value unique to this run, so finding it in the preview proves it came from here.
        var marker = "Probe" + Guid.NewGuid().ToString("N")[..8];

        var values = new List<object>();
        foreach (var field in type.GetProperty("fields").EnumerateArray())
        {
            var key = field.GetProperty("key").GetString() ?? string.Empty;
            var options = field.GetProperty("options").EnumerateArray()
                .Select(o => o.GetString()).Where(o => o is not null).ToList();

            var value = key switch
            {
                "award_date" => "2026-01-01",
                "expiry_date" => "2028-01-01",   // two years, so the derived term is assertable
                _ when options.Count > 0 => options[0]!,
                _ when field.GetProperty("fieldType").GetInt32() is 2 or 3 => "1000",
                _ => marker,
            };
            values.Add(new { contractTypeFieldId = field.GetProperty("id").GetInt32(), value });
        }

        var payload = JsonSerializer.Serialize(new
        {
            // Deliberately not the marker itself: the title is an editable field on the draft
            // form, so a search for the marker must not match it.
            title = "Request " + marker,
            description = "Raised by a browser test",
            meetingNumber = "CC/2026/01",
            nature = 0,
            contractTypeId = typeId,
            amount = 1000,
            fieldValues = values,
            kpis = Array.Empty<object>(),
        });

        var response = await http.PostAsync("/api/contract-requests",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();

        using var created = JsonDocument.Parse(body);
        var data = created.RootElement.GetProperty("data");
        return new Seeded(data.GetProperty("id").GetInt32(),
            data.GetProperty("requestReference").GetString()!, marker, typeId);
    }

    private static async Task<string> StatusOfAsync(int id)
    {
        using var http = await ApiAsync(Initiator);
        using var body = JsonDocument.Parse(await http.GetStringAsync($"/api/contract-requests/{id}"));
        return body.RootElement.GetProperty("data").GetProperty("status").GetInt32() == 0
            ? "Open"
            : "Drafted";
    }

    private static async Task<List<string>> TemplateNamesOfOtherTypesAsync(int contractTypeId)
    {
        using var http = await ApiAsync("pkavule");   // a template author, to read the library
        using var body = JsonDocument.Parse(
            await http.GetStringAsync("/api/templates?status=2&page=1&pageSize=200"));

        return body.RootElement.GetProperty("data").EnumerateArray()
            .Where(t => t.TryGetProperty("contractTypeId", out var ct)
                        && ct.ValueKind == JsonValueKind.Number
                        && ct.GetInt32() != contractTypeId)
            .Select(t => t.GetProperty("name").GetString()!)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Take(5).ToList();
    }

    /// <summary>Best effort: a request that has been drafted cannot be removed, by design.</summary>
    private static async Task DeleteRequestAsync(int id)
    {
        try
        {
            using var http = await ApiAsync(Initiator);
            await http.DeleteAsync($"/api/contract-requests/{id}");
        }
        catch
        {
            // A cleanup failure must not mask the assertion that already ran.
        }
    }

    private static async Task<HttpClient> ApiAsync(string username)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri(AppFixture.ApiBase) };

        var login = await http.PostAsync("/api/user/login",
            new StringContent(JsonSerializer.Serialize(new { username, password = Password }),
                Encoding.UTF8, "application/json"));

        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = body.RootElement.GetProperty("data").GetProperty("token").GetString();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }
}
