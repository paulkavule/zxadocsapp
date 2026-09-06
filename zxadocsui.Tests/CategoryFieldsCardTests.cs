using Microsoft.Playwright;

namespace zxadocsui.Tests;

// ZD-126. The category fields card on /workflows, and what an author then sees on step 1 of
// /createdocument. The round trip is the point: a field saved here has to come back with its
// type and its required flag, because the wizard gate reads both.
[Collection("app")]
[Trait("Category", "Ui")]
public class CategoryFieldsCardTests(AppFixture app)
{
    private void SkipIfAppDown() =>
        Assert.SkipWhen(!app.AppReachable, app.SkipReason ?? "app not running");

    [Fact]
    public async Task The_card_appears_only_once_a_category_is_chosen()
    {
        SkipIfAppDown();
        await using var page = await app.SignedInPageAsync();
        await page.GotoAsync($"{AppFixture.UiBase}/workflows",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        await Assertions.Expect(page.GetByText("Category fields")).ToHaveCountAsync(0);

        var (_, category) = await ChooseTypeAndCategoryAsync(page);
        Assert.SkipWhen(category is null, "the organisation has no document type with a category configured");

        await Assertions.Expect(page.GetByText("Category fields")).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task A_saved_field_comes_back_with_its_type_and_required_flag()
    {
        SkipIfAppDown();
        var admin = AppFixture.RoleAdmin;
        Assert.SkipWhen(admin is null, "ZXADOCS_ROLE_ADMIN/_PASSWORD are not set");

        await using var page = await app.SignedInPageAsync(admin!.Value.User, admin.Value.Password);
        await page.GotoAsync($"{AppFixture.UiBase}/workflows",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        var (type, category) = await ChooseTypeAndCategoryAsync(page);
        Assert.SkipWhen(category is null, "the organisation has no document type with a category configured");

        var save = page.GetByRole(AriaRole.Button, new() { Name = "Save fields" });
        Assert.SkipWhen(await save.CountAsync() == 0, "this account does not hold ManageRole");

        var name = "[e2e] region " + Guid.NewGuid().ToString("N")[..6];
        await AddDropdownAsync(page, name, "North, South");
        await save.ClickAsync();
        await page.WaitForTimeoutAsync(3000);

        // A fresh visit rather than the rows on screen: only the server issues the FieldId, and
        // only a re-read proves the row was actually stored. The same type and category by name,
        // because "the first one in the list" is not a promise across two page loads.
        await page.GotoAsync($"{AppFixture.UiBase}/workflows",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await PickNamedOptionAsync(page, "Document Type", type!);
        await PickNamedOptionAsync(page, "Document category", category!);
        await page.WaitForTimeoutAsync(2000);

        var row = await RowForAsync(page, name);
        Assert.True(row is not null, $"the saved field was not listed again. Names on screen: {await NamesAsync(page)}");
        Assert.Equal("Dropdown", (await row!.Locator(".mud-select input").First.InputValueAsync()).Trim());
        Assert.Equal("North, South", await row.Locator("input").Nth(2).InputValueAsync());
    }

    [Fact]
    public async Task An_author_cannot_leave_step_one_with_a_required_field_empty()
    {
        SkipIfAppDown();
        var admin = AppFixture.RoleAdmin;
        Assert.SkipWhen(admin is null, "ZXADOCS_ROLE_ADMIN/_PASSWORD are not set");

        await using var adminPage = await app.SignedInPageAsync(admin!.Value.User, admin.Value.Password);
        await adminPage.GotoAsync($"{AppFixture.UiBase}/workflows",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        var (type, category) = await ChooseTypeAndCategoryAsync(adminPage);
        Assert.SkipWhen(category is null, "the organisation has no document type with a category configured");

        var save = adminPage.GetByRole(AriaRole.Button, new() { Name = "Save fields" });
        Assert.SkipWhen(await save.CountAsync() == 0, "this account does not hold ManageRole");

        var name = "[e2e] required " + Guid.NewGuid().ToString("N")[..6];
        await AddRequiredTextAsync(adminPage, name);
        await save.ClickAsync();
        await adminPage.WaitForTimeoutAsync(3000);

        try
        {
            await using var authorPage = await app.SignedInPageAsync();
            await authorPage.GotoAsync($"{AppFixture.UiBase}/createdocument",
                new() { WaitUntil = WaitUntilState.NetworkIdle });

            await authorPage.GetByRole(AriaRole.Textbox, new() { Name = "Title" })
                .FillAsync("[e2e] required field " + Guid.NewGuid().ToString("N")[..6]);
            await PickNamedOptionAsync(authorPage, "Document Type", type!);
            await PickNamedOptionAsync(authorPage, "Document category", category!);

            await authorPage.GetByRole(AriaRole.Button, new() { Name = "Next" }).First.ClickAsync();

            // The gate names the field, so the author is told which one is missing.
            await Assertions.Expect(authorPage.GetByText($"{name} is required"))
                .ToBeVisibleAsync(new() { Timeout = 15_000 });
        }
        finally
        {
            // A required field left behind would block every other wizard test on this category.
            await RemoveFieldAsync(adminPage, name);
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Picks the first real type, then the first real category under it, and names both.</summary>
    private static async Task<(string? Type, string? Category)> ChooseTypeAndCategoryAsync(IPage page)
    {
        var type = await PickFirstRealOptionAsync(page, "Document Type");
        if (type is null) return (null, null);
        return (type, await PickFirstRealOptionAsync(page, "Document category"));
    }

    /// <summary>Opens a MudSelect, takes the first option that is not the placeholder, returns its text.</summary>
    private static async Task<string?> PickFirstRealOptionAsync(IPage page, string label)
    {
        var options = await OpenAsync(page, label);
        if (options is null) return null;

        for (var i = 0; i < await options.CountAsync(); i++)
        {
            var option = options.Nth(i);
            var text = (await option.InnerTextAsync()).Trim();
            if (text is "" or "Select") continue;

            await option.ClickAsync();
            await page.WaitForTimeoutAsync(2500); // the cascade fetches the next list
            return text;
        }

        await page.Keyboard.PressAsync("Escape");
        return null;
    }

    private static async Task PickNamedOptionAsync(IPage page, string label, string name)
    {
        var options = await OpenAsync(page, label);
        Assert.True(options is not null, $"the {label} picker never opened");

        await options!.Filter(new() { HasTextString = name }).First.ClickAsync(new() { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(2500);
    }

    /// <summary>Opens one MudSelect and returns its option list, or null when nothing appeared.</summary>
    private static async Task<ILocator?> OpenAsync(IPage page, string label)
    {
        await page.GetByLabel(label).First.ClickAsync();
        var options = page.Locator("div.mud-popover-open .mud-list-item");
        try
        {
            await options.First.WaitForAsync(new() { Timeout = 15_000 });
            return options;
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    /// <summary>The field row whose name input holds this value. Inputs carry no text, so no text match.</summary>
    private static async Task<ILocator?> RowForAsync(IPage page, string name)
    {
        var rows = page.Locator("tbody tr");
        for (var i = 0; i < await rows.CountAsync(); i++)
        {
            var row = rows.Nth(i);
            var inputs = row.Locator("input");
            if (await inputs.CountAsync() > 0 && await inputs.First.InputValueAsync() == name)
                return row;
        }
        return null;
    }

    private static async Task<string> NamesAsync(IPage page)
    {
        var inputs = page.Locator("tbody tr td:first-child input");
        var names = new List<string>();
        for (var i = 0; i < await inputs.CountAsync(); i++)
            names.Add(await inputs.Nth(i).InputValueAsync());
        return string.Join(" | ", names);
    }

    /// <summary>Adds a row, names it and ticks Required. The type stays the default, Text.</summary>
    private static async Task AddRequiredTextAsync(IPage page, string name)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add field" }).ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var row = page.Locator("tbody tr").Last;
        await row.Locator("input").First.FillAsync(name);
        await row.Locator("input[type='checkbox']").First.CheckAsync();
    }

    /// <summary>Deletes the named row and saves, so the category is left as it was found.</summary>
    private static async Task RemoveFieldAsync(IPage page, string name)
    {
        var row = await RowForAsync(page, name);
        if (row is null) return;

        await row.GetByLabel("Remove field").ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Save fields" }).ClickAsync();
        await page.WaitForTimeoutAsync(3000);
    }

    /// <summary>Adds a row, names it, switches it to Dropdown and fills its options.</summary>
    private static async Task AddDropdownAsync(IPage page, string name, string optionsCsv)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Add field" }).ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var row = page.Locator("tbody tr").Last;
        await row.Locator("input").First.FillAsync(name);

        // The type picker is a MudSelect: its options are list items in a popover, not role=option.
        await row.Locator(".mud-select").First.ClickAsync();
        await page.Locator("div.mud-popover-open .mud-list-item", new() { HasTextString = "Dropdown" })
            .First.ClickAsync(new() { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(500);

        await row.Locator("input").Nth(2).FillAsync(optionsCsv);
    }
}
