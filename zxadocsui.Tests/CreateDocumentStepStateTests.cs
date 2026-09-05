using Microsoft.Playwright;

namespace zxadocsui.Tests;

// ZD-125. MudStep destroys the content of an inactive step, so every component in the create
// wizard is rebuilt on return. These drive the path the bug was reported on: step 1, into step 2,
// on to step 3, then back.
//
// Assertions read the rendered step content rather than input values: the pickers are MudSelects,
// whose chosen value is rendered text and not the value of an input element.
[Collection("app")]
[Trait("Category", "Ui")]
public class CreateDocumentStepStateTests(AppFixture app)
{
    private void SkipIfAppDown() =>
        Assert.SkipWhen(!app.AppReachable, app.SkipReason ?? "app not running");

    [Fact]
    public async Task Step_two_keeps_its_actors_after_a_trip_to_step_three()
    {
        SkipIfAppDown();
        var (page, _, _, _) = await OnStepTwoAsync();
        await using var owned = page;

        // Step 3 is gated on a workflow existing, so the actor has to be real work, not a no-op.
        var added = await AddFirstActorAsync(page);
        Assert.SkipWhen(!added, "no user matched the actor search, so no workflow can be built");

        var before = await StepContentAsync(page);
        Assert.SkipWhen(before.Length == 0, "step 2 rendered nothing to preserve");

        await GoToStepAsync(page, "Step 3");
        await GoToStepAsync(page, "Step 2");

        var after = await StepContentAsync(page);

        // The reported symptom: coming back leaves the actor list empty.
        Assert.NotEqual(string.Empty, after);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Step_one_keeps_its_type_and_category_after_a_trip_to_step_two()
    {
        SkipIfAppDown();
        var (page, type, category, before) = await OnStepTwoAsync();
        await using var owned = page;

        await GoToStepAsync(page, "Step 1");
        var after = await StepContentAsync(page);

        // The category list is only filled by the type picker, and the category's extra fields only
        // by the category picker, so a rebuilt step 1 came back missing both.
        Assert.Contains(type, after);
        Assert.Contains(category, after);
        Assert.Equal(before, after);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Signs in, fills step 1 with the first real type and category, and advances.</summary>
    private async Task<(IPage Page, string Type, string Category, string StepOne)> OnStepTwoAsync()
    {
        var page = await app.SignedInPageAsync();
        await page.GotoAsync($"{AppFixture.UiBase}/createdocument",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        await page.GetByRole(AriaRole.Textbox, new() { Name = "Title" })
            .FillAsync("[e2e] step state " + Guid.NewGuid().ToString("N")[..6]);

        var type = await PickFirstRealOptionAsync(page, "Document Type");
        Assert.SkipWhen(type is null, "the organisation has no document types configured");

        var category = await PickFirstRealOptionAsync(page, "Document category");
        Assert.SkipWhen(category is null, "the selected document type has no categories configured");

        var stepOne = await StepContentAsync(page);

        await GoToStepAsync(page, "Step 2");
        return (page, type!, category!, stepOne);
    }

    /// <summary>Opens a MudSelect, takes the first option that is not the placeholder, returns its text.</summary>
    private static async Task<string?> PickFirstRealOptionAsync(IPage page, string label)
    {
        await page.GetByLabel(label).ClickAsync();
        var options = page.Locator("div.mud-popover-open .mud-list-item");
        await options.First.WaitForAsync(new() { Timeout = 15_000 });

        for (var i = 0; i < await options.CountAsync(); i++)
        {
            var option = options.Nth(i);
            var text = (await option.InnerTextAsync()).Trim();
            if (text is "" or "Select") continue;

            await option.ClickAsync();
            await page.WaitForTimeoutAsync(2000); // the cascade fetches the next list
            return text;
        }

        await page.Keyboard.PressAsync("Escape");
        return null;
    }

    /// <summary>Types into the actor autocomplete and takes the first suggestion. False when none.</summary>
    private static async Task<bool> AddFirstActorAsync(IPage page)
    {
        var actor = page.GetByLabel("Actor");
        if (await actor.CountAsync() == 0)
            return true; // the category's default workflow already produced a chain

        await actor.ClickAsync();
        await actor.FillAsync("kav"); // SearchUsers ignores anything shorter than three characters

        var suggestions = page.Locator("div.mud-popover-open .mud-list-item");
        try
        {
            await suggestions.First.WaitForAsync(new() { Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            return false;
        }

        await suggestions.First.ClickAsync();
        await page.WaitForTimeoutAsync(2000);
        return true;
    }

    /// <summary>Clicks a step header. The stepper is NonLinear, so headers are a supported route.</summary>
    private static async Task GoToStepAsync(IPage page, string title)
    {
        await page.Locator(".mud-step-label", new() { HasTextString = title }).First.ClickAsync();
        await page.WaitForTimeoutAsync(3000);
    }

    /// <summary>The active step's rendered text, whitespace-collapsed so it compares cleanly.</summary>
    private static async Task<string> StepContentAsync(IPage page)
    {
        var text = await page.Locator(".mud-stepper-content").InnerTextAsync();
        return string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
