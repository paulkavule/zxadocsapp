using Microsoft.Playwright;

namespace zxadocsui.Tests;

// The contract type selector on /templates/new (ZD-116). Runs as the default authoring account,
// which holds CreateTemplate and can now read the contract type catalogue.
[Collection("app")]
[Trait("Category", "Ui")]
public class TemplateContractTypeUiTests(AppFixture app)
{
    private void SkipIfAppDown() =>
        Assert.SkipWhen(!app.AppReachable, app.SkipReason ?? "app not running");

    [Fact]
    public async Task A_non_contract_category_does_not_offer_a_contract_type()
    {
        SkipIfAppDown();
        await using var page = await NewFormAsync();

        await ChooseCategoryAsync(page, "NDA");

        // Not merely hidden: the field must not be in the page at all.
        await Assertions.Expect(SelectByLabel(page, "Contract type")).ToHaveCountAsync(0,
            new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Add field" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task A_contract_category_offers_a_contract_type()
    {
        SkipIfAppDown();
        await using var page = await NewFormAsync();

        await ChooseCategoryAsync(page, "Contract");

        await Assertions.Expect(SelectByLabel(page, "Contract type")).ToHaveCountAsync(1,
            new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task Choosing_a_type_replaces_the_field_builder_with_the_types_fields()
    {
        SkipIfAppDown();
        await using var page = await NewFormAsync();
        await ChooseCategoryAsync(page, "Contract");

        var chosen = await ChooseFirstContractTypeAsync(page);
        Assert.SkipWhen(chosen is null, "this organisation has no contract types to choose");

        // The builder is gone: fields belong to the type and are edited there.
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Add field" }))
            .ToHaveCountAsync(0, new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByText($"come from the {chosen} contract type"))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task Moving_off_a_contract_category_drops_the_chosen_type()
    {
        SkipIfAppDown();
        await using var page = await NewFormAsync();
        await ChooseCategoryAsync(page, "Contract");
        var chosen = await ChooseFirstContractTypeAsync(page);
        Assert.SkipWhen(chosen is null, "this organisation has no contract types to choose");

        await ChooseCategoryAsync(page, "NDA");

        // Otherwise a template would be created against a type the author could no longer see.
        await Assertions.Expect(SelectByLabel(page, "Contract type")).ToHaveCountAsync(0,
            new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Add field" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    // ---------------------------------------------------------------- helpers

    // MudBlazor renders these selects with id="false" on the input, so the label's for= points at
    // nothing and GetByLabel cannot resolve them. Locate the control by its label text instead.
    private static ILocator SelectByLabel(IPage page, string label) =>
        page.Locator($"div.mud-input-control:has(label:text-is('{label}'))");

    private async Task<IPage> NewFormAsync()
    {
        var page = await app.SignedInPageAsync();
        await page.GotoAsync($"{AppFixture.UiBase}/templates/new",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await SelectByLabel(page, "Category").WaitForAsync(new() { Timeout = 30_000 });
        return page;
    }

    private static async Task ChooseCategoryAsync(IPage page, string name)
    {
        await SelectByLabel(page, "Category").ClickAsync();
        await page.Locator("div.mud-list-item").Filter(new() { HasTextString = name }).First
            .ClickAsync(new() { Timeout = 15_000 });
        await ClosePopoverAsync(page);
    }

    /// <summary>Picks the first real type; returns its name, or null when the org has none.</summary>
    private static async Task<string?> ChooseFirstContractTypeAsync(IPage page)
    {
        await SelectByLabel(page, "Contract type").ClickAsync();
        // The first entry is the "None" escape hatch, so a real type is the second.
        var options = page.Locator("div.mud-list-item");
        await options.First.WaitForAsync(new() { Timeout = 15_000 });
        if (await options.CountAsync() < 2) { await ClosePopoverAsync(page); return null; }

        var name = (await options.Nth(1).InnerTextAsync()).Trim();
        await options.Nth(1).ClickAsync();
        await ClosePopoverAsync(page);
        return name;
    }

    /// <summary>MudBlazor leaves the popover over the rest of the form; its overlay dismisses it.</summary>
    private static async Task ClosePopoverAsync(IPage page)
    {
        var overlay = page.Locator("div.mud-overlay");
        if (await overlay.CountAsync() > 0)
        {
            try { await overlay.First.ClickAsync(new() { Timeout = 3000 }); }
            catch (TimeoutException) { /* already gone */ }
        }
        await page.WaitForTimeoutAsync(500);
    }
}
