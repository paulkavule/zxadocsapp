using Microsoft.Playwright;

namespace zxadocsui.Tests;

// ZD-131. The Settings section is platform administration, so an organisation user must not see it
// or reach its routes, and a system user must be able to switch which tenant it is acting on.
[Collection("app")]
[Trait("Category", "Ui")]
public class SettingsSectionTests(AppFixture app)
{
    private void SkipIfAppDown() =>
        Assert.SkipWhen(!app.AppReachable, app.SkipReason ?? "app not running");

    [Fact]
    public async Task An_organisation_user_sees_no_system_navigation()
    {
        SkipIfAppDown();
        await using var page = await app.SignedInPageAsync();

        // The nav is built after the permission fetch, so give that render a moment to land.
        await page.WaitForTimeoutAsync(3000);

        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Organisations" })).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "System users" })).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByLabel("Acting on")).ToHaveCountAsync(0);

        // The converse of hiding these from a system user (ZD-135): an organisation user does the
        // document work, so the nav change must not have taken them away from everyone.
        await Assertions.Expect(page.Locator("a[href='/documents']")).ToHaveCountAsync(1);

        // Workflow definitions are permission-gated now, and this account holds no workflow
        // permission, so the link is absent for a second and separate reason.
        await Assertions.Expect(page.Locator("a[href='/workflows']")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task An_organisation_user_deep_linking_into_settings_is_bounced()
    {
        SkipIfAppDown();
        await using var page = await app.SignedInPageAsync();

        await page.GotoAsync($"{AppFixture.UiBase}/settings/organisations",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.WaitForTimeoutAsync(4000);

        // Client-side redirect only; the API refuses the calls regardless of what renders.
        Assert.EndsWith("/dashboard", page.Url.TrimEnd('/'));
    }

    [Fact]
    public async Task A_system_user_sees_the_section_and_the_organisation_picker()
    {
        SkipIfAppDown();
        var admin = AppFixture.SystemAdmin;
        Assert.SkipWhen(admin is null, "ZXADOCS_SYSTEM_ADMIN/_PASSWORD are not set");

        await using var page = await app.SignedInPageAsync(admin!.Value.User, admin.Value.Password);
        await page.WaitForTimeoutAsync(3000);

        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Organisations" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByLabel("Acting on")).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task The_organisation_list_loads_for_a_system_user()
    {
        SkipIfAppDown();
        var admin = AppFixture.SystemAdmin;
        Assert.SkipWhen(admin is null, "ZXADOCS_SYSTEM_ADMIN/_PASSWORD are not set");

        await using var page = await app.SignedInPageAsync(admin!.Value.User, admin.Value.Password);
        await page.WaitForTimeoutAsync(3000);
        await page.GetByRole(AriaRole.Link, new() { Name = "Organisations" }).ClickAsync();
        await page.WaitForTimeoutAsync(4000);

        Assert.Contains("/settings/organisations", page.Url);
        // The seeded system organisation is always there, so the table is never legitimately empty.
        await Assertions.Expect(page.GetByText("New organisation"))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        Assert.Contains("System", await page.Locator("tbody").First.InnerTextAsync());
    }

    [Fact]
    public async Task Switching_organisation_reloads_the_department_list()
    {
        SkipIfAppDown();
        var admin = AppFixture.SystemAdmin;
        Assert.SkipWhen(admin is null, "ZXADOCS_SYSTEM_ADMIN/_PASSWORD are not set");

        await using var page = await app.SignedInPageAsync(admin!.Value.User, admin.Value.Password);
        await page.GotoAsync($"{AppFixture.UiBase}/settings/departments",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.WaitForTimeoutAsync(4000);

        // The card's own subtitle. "p.text-xs" also matches the sidebar's role label, which is
        // literally "System Admin" - and that made this assert against the wrong element.
        var subtitle = page.Locator("h1:text-is('Departments') + p");
        var before = await subtitle.InnerTextAsync();

        var picked = await PickAnOrganisationAsync(page);
        Assert.SkipWhen(picked is null, "the picker offered no organisation");

        // The header the request carries is what changed, not the URL - so the proof is that the
        // page now names the chosen tenant.
        await Assertions.Expect(subtitle).ToContainTextAsync(picked!, new() { Timeout = 15_000 });
        Assert.NotEqual(before, await subtitle.InnerTextAsync());
    }

    [Fact]
    public async Task All_organisations_tells_the_operator_to_choose_one()
    {
        SkipIfAppDown();
        var admin = AppFixture.SystemAdmin;
        Assert.SkipWhen(admin is null, "ZXADOCS_SYSTEM_ADMIN/_PASSWORD are not set");

        await using var page = await app.SignedInPageAsync(admin!.Value.User, admin.Value.Password);
        await page.GotoAsync($"{AppFixture.UiBase}/settings/departments",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.WaitForTimeoutAsync(4000);

        var options = page.Locator("div.mud-popover-open .mud-list-item");
        await page.GetByLabel("Acting on").ClickAsync();
        await options.First.WaitForAsync(new() { Timeout = 15_000 });
        await options.Filter(new() { HasTextString = "All organisations" }).First.ClickAsync(new() { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(3000);

        await Assertions.Expect(page.GetByText("Departments belong to one organisation"))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task The_workflow_list_shows_the_selected_organisations_documents()
    {
        SkipIfAppDown();
        var admin = AppFixture.SystemAdmin;
        Assert.SkipWhen(admin is null, "ZXADOCS_SYSTEM_ADMIN/_PASSWORD are not set");

        await using var page = await app.SignedInPageAsync(admin!.Value.User, admin.Value.Password);
        await page.WaitForTimeoutAsync(3000);

        // By href, not by name: the NavLink's accessible name carries the icon's leading space, and
        // there is also a top-level /workflows link for authors. The screen needs System Support or
        // System Viewer, so skip when the account holds neither.
        var link = page.Locator("a[href='/settings/workflows']");
        Assert.SkipWhen(await link.CountAsync() == 0, "this account holds no support or viewer role");

        await link.First.ClickAsync();
        await page.WaitForTimeoutAsync(3000);

        var picked = await PickAnOrganisationAsync(page);
        Assert.SkipWhen(picked is null, "the picker offered no organisation");
        await page.WaitForTimeoutAsync(3000);

        await Assertions.Expect(page.Locator("h1:text-is('Workflows') + p"))
            .ToContainTextAsync(picked!, new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task The_reassign_panel_says_what_it_will_not_touch()
    {
        SkipIfAppDown();
        var admin = AppFixture.SystemAdmin;
        Assert.SkipWhen(admin is null, "ZXADOCS_SYSTEM_ADMIN/_PASSWORD are not set");

        await using var page = await app.SignedInPageAsync(admin!.Value.User, admin.Value.Password);
        await page.WaitForTimeoutAsync(3000);

        var link = page.Locator("a[href='/settings/workflows']");
        Assert.SkipWhen(await link.CountAsync() == 0, "this account holds no support or viewer role");

        await link.First.ClickAsync();
        await page.WaitForTimeoutAsync(3000);
        Assert.SkipWhen(await PickAnOrganisationAsync(page) is null, "the picker offered no organisation");
        await page.WaitForTimeoutAsync(3000);

        var reassign = page.GetByRole(AriaRole.Button, new() { Name = "Reassign" });
        Assert.SkipWhen(await reassign.CountAsync() == 0, "the organisation has no workflows to reassign");

        await reassign.First.ClickAsync();
        await page.WaitForTimeoutAsync(2500);

        // The chain it is about to change: the roles the category expects beside the users the
        // document actually names.
        await Assertions.Expect(page.GetByText("Configured workflow"))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(page.Locator(".mud-drawer th", new() { HasTextString = "Role" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Most categories carry no workflow template, so a role resolved only from that template
        // left this column empty on nearly every document.
        var roleCells = page.Locator(".mud-drawer tbody tr td:nth-child(2)");
        await roleCells.First.WaitForAsync(new() { Timeout = 15_000 });
        var roles = (await roleCells.AllInnerTextsAsync()).Select(r => r.Trim()).ToList();
        Assert.True(roles.Any(r => r.Length > 0 && r != "-"),
            $"no actor carried a role: {string.Join(" | ", roles)}");

        // Reassignment substitutes the actor, and is deliberately not signing; the panel says both.
        await Assertions.Expect(page.GetByText("takes this person's place"))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByText("It does not sign, stamp or archive"))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByLabel("Replace with")).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task The_reassign_panel_offers_the_acting_organisations_users()
    {
        SkipIfAppDown();
        var admin = AppFixture.SystemAdmin;
        Assert.SkipWhen(admin is null, "ZXADOCS_SYSTEM_ADMIN/_PASSWORD are not set");

        await using var page = await app.SignedInPageAsync(admin!.Value.User, admin.Value.Password);
        await page.WaitForTimeoutAsync(3000);

        var link = page.Locator("a[href='/settings/workflows']");
        Assert.SkipWhen(await link.CountAsync() == 0, "this account holds no support or viewer role");

        await link.First.ClickAsync();
        await page.WaitForTimeoutAsync(3000);
        Assert.SkipWhen(await PickAnOrganisationAsync(page) is null, "the picker offered no organisation");
        await page.WaitForTimeoutAsync(3000);

        var reassign = page.GetByRole(AriaRole.Button, new() { Name = "Reassign" });
        Assert.SkipWhen(await reassign.CountAsync() == 0, "the organisation has no workflows to reassign");

        await reassign.First.ClickAsync();
        await page.WaitForTimeoutAsync(2500);
        await page.GetByLabel("Replace with").ClickAsync();

        var options = page.Locator("div.mud-popover-open .mud-list-item");
        await options.First.WaitForAsync(new() { Timeout = 15_000 });
        var names = await options.AllInnerTextsAsync();

        // The list options endpoint resolved the caller without the acting-organisation header, so
        // it answered for the system organisation: the only candidate offered was the system
        // account itself, who cannot hold a customer's document.
        Assert.DoesNotContain("System Administrator", names.Select(n => n.Trim()));
        Assert.True(names.Count > 1, $"expected the organisation's users, got: {string.Join(", ", names)}");
    }

    /// <summary>Picks the first real organisation from the header picker and returns its name.</summary>
    private static async Task<string?> PickAnOrganisationAsync(IPage page)
    {
        await page.GetByLabel("Acting on").ClickAsync();
        var options = page.Locator("div.mud-popover-open .mud-list-item");
        try
        {
            await options.First.WaitForAsync(new() { Timeout = 15_000 });
        }
        catch (TimeoutException)
        {
            return null;
        }

        for (var i = 0; i < await options.CountAsync(); i++)
        {
            var text = (await options.Nth(i).InnerTextAsync()).Trim();
            if (text is "" or "All organisations") continue;

            await options.Nth(i).ClickAsync();
            await page.WaitForTimeoutAsync(3000);
            return text;
        }

        await page.Keyboard.PressAsync("Escape");
        return null;
    }
}
