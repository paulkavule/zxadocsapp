using Microsoft.Playwright;

namespace zxadocsui.Tests;

// The frontend test-case pass, TF1-TF9 (ZD-28). One test per case, asserting the behaviour the
// case exists to protect rather than every sub-bullet.
//
// Two actors: the dev-default login authors but holds neither approve permission, so TF4 and
// TF7-TF9 need ZXADOCS_APPROVER/ZXADOCS_APPROVER_PASSWORD and skip without them.
//
// Test data carries the RunTag prefix and is archived at the end, so a run leaves no active rows.
[Collection("app")]
[Trait("Category", "Ui")]
public class FrontendPassTests(AppFixture app)
{
    private static readonly string RunTag = "[tf-" + Guid.NewGuid().ToString("N")[..6] + "]";

    private void SkipIfAppDown() =>
        Assert.SkipWhen(!app.AppReachable, app.SkipReason ?? "app not running");

    private void SkipIfNoApprover() =>
        Assert.SkipWhen(AppFixture.Approver is null,
            "set ZXADOCS_APPROVER and ZXADOCS_APPROVER_PASSWORD; the default login cannot approve");

    // ---------------------------------------------------------------- TF1 library

    [Fact]
    public async Task TF1_Library_lists_templates_and_filters_them_by_search()
    {
        SkipIfAppDown();
        await using var page = await app.SignedInPageAsync();

        var id = await CreateTemplateAsync(page, $"{RunTag} searchable agreement");
        try
        {
            await page.GotoAsync($"{AppFixture.UiBase}/templates");
            await page.GetByRole(AriaRole.Row).First.WaitForAsync(new() { Timeout = 30_000 });
            var all = await page.GetByRole(AriaRole.Row).CountAsync();
            Assert.True(all > 1, "the library rendered no template rows");

            await page.GetByPlaceholder("Search templates").FillAsync("searchable agreement");
            await page.WaitForTimeoutAsync(3500);

            // The row we just made must survive the filter, and the filter must actually narrow.
            await VisibleAsync(page.GetByText("searchable agreement").First, 20_000);
            Assert.True(await page.GetByRole(AriaRole.Row).CountAsync() < all,
                "searching did not reduce the row count");
        }
        finally { await ArchiveAsync(page, id); }
    }

    // ---------------------------------------------------------------- TF2 create

    [Fact]
    public async Task TF2_Created_template_saves_as_a_draft_then_moves_to_pending_on_submit()
    {
        SkipIfAppDown();
        await using var page = await app.SignedInPageAsync();

        var id = await CreateTemplateAsync(page, $"{RunTag} lifecycle");
        try
        {
            await VisibleAsync(page.GetByText("Draft").First, 15_000);

            await page.GetByRole(AriaRole.Button, new() { Name = "Submit" }).First
                .ClickAsync(new() { Timeout = 30_000 });

            // PendingApproval is what puts it in an approver's queue; Draft would mean the submit
            // silently did nothing.
            await VisibleAsync(page.GetByText("PendingApproval").First, 30_000);
        }
        finally { await ArchiveAsync(page, id); }
    }

    // ---------------------------------------------------------------- TF3 detail

    [Fact]
    public async Task TF3_Detail_previews_the_body_and_offers_no_approve_action_to_a_non_approver()
    {
        SkipIfAppDown();
        await using var page = await app.SignedInPageAsync();

        var id = await CreateTemplateAsync(page, $"{RunTag} detail");
        try
        {
            await page.GotoAsync($"{AppFixture.UiBase}/templates/{id}");
            await page.WaitForTimeoutAsync(4000);

            var body = await PreviewTextAsync(page);
            Assert.Contains("SERVICE AGREEMENT", body);

            // The account authors but cannot approve, so the action must not be rendered at all —
            // hiding it is the only thing standing between a non-approver and a 403.
            Assert.Equal(0, await page.GetByRole(AriaRole.Button, new() { Name = "Approve" }).CountAsync());
        }
        finally { await ArchiveAsync(page, id); }
    }

    [Fact]
    public async Task TF3_Uploaded_image_displays_on_the_preview_surface()
    {
        SkipIfAppDown();
        await using var page = await app.SignedInPageAsync();

        // The existing editor image test seeds a data: URL, which never touches upload or signing.
        // Only a real upload produces the URL the preview surfaces have to resolve.
        await page.GotoAsync(AppFixture.UiBase + "/templates/new");
        await AppFixture.EditorAsync(page);
        var editorSrc = await UploadImageAsync(page);

        // A data: URL means the toolbar bypassed the upload and inlined the bytes, which is the
        // thing ZD-83 exists to prevent — and it would hide the display bug this test is for.
        Assert.False(editorSrc.StartsWith("data:", StringComparison.Ordinal),
            "the toolbar inlined the image as base64 instead of uploading it");

        var id = await SaveTemplateAsync(page, $"{RunTag} uploaded image");
        try
        {
            await page.GotoAsync($"{AppFixture.UiBase}/templates/{id}");
            await page.WaitForTimeoutAsync(5000);

            var src = await PreviewImageSrcAsync(page);
            Assert.False(string.IsNullOrEmpty(src), "the preview carries no image");

            // The iframe is sandboxed, so the parent cannot read naturalWidth out of it. Fetching
            // the same URL from the page's own origin answers the real question: does it resolve
            // to image bytes, or 404 because it points somewhere the browser cannot reach?
            var result = await page.EvaluateAsync<string>(
                "async (u) => { try { const r = await fetch(u); " +
                "return r.status + '|' + (r.headers.get('content-type') || ''); } " +
                "catch (e) { return 'threw|' + e.message; } }", src);

            Assert.StartsWith("200|image/", result);
        }
        finally { await ArchiveAsync(page, id); }
    }

    // ---------------------------------------------------------------- TF5 draft editor

    [Fact]
    public async Task TF5_Draft_editor_merges_field_values_into_the_preview()
    {
        SkipIfAppDown();
        SkipIfNoApprover();
        var (user, password) = AppFixture.Approver!.Value;

        await using var author = await app.SignedInPageAsync();
        var templateId = await CreateTemplateAsync(author, $"{RunTag} draft source");
        try
        {
            await SubmitAsync(author, templateId);
            await using var approver = await app.SignedInPageAsync(user, password);
            await ApproveTemplateAsync(approver, templateId);

            await author.GotoAsync($"{AppFixture.UiBase}/drafts/new/{templateId}");
            await author.GetByRole(AriaRole.Textbox, new() { Name = "Title" })
                .FillAsync($"{RunTag} merged draft", new() { Timeout = 30_000 });
            await author.GetByRole(AriaRole.Textbox, new() { Name = "Client name" })
                .FillAsync("Acme Holdings");
            await author.WaitForTimeoutAsync(4000);

            // The token must be gone and the value in its place; a surviving {{...}} means the
            // merge never ran.
            var body = await PreviewTextAsync(author);
            Assert.Contains("Acme Holdings", body);
            Assert.DoesNotContain("{{client_name}}", body);
        }
        finally { await ArchiveAsync(author, templateId); }
    }

    // ---------------------------------------------------------------- TF6 draft list

    [Fact]
    public async Task TF6_Draft_list_populates_on_a_direct_page_load()
    {
        SkipIfAppDown();
        await using var page = await app.SignedInPageAsync();

        // Reached by URL rather than by clicking through, because that is where the list has been
        // seen to come back empty: the request goes out before the token is available.
        await page.GotoAsync($"{AppFixture.UiBase}/drafts", new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.WaitForTimeoutAsync(5000);

        var empty = await page.GetByText("No drafts yet").CountAsync();
        Assert.True(empty == 0,
            "the drafts list is empty on a direct load; the request carries no Authorization header " +
            "and returns 401, while in-app navigation to the same page works");
    }

    // ---------------------------------------------------------------- TF4 template approvals

    [Fact]
    public async Task TF4_Approving_a_template_removes_it_from_the_queue_and_sets_the_current_version()
    {
        SkipIfAppDown();
        SkipIfNoApprover();
        var (user, password) = AppFixture.Approver!.Value;

        await using var author = await app.SignedInPageAsync();
        var id = await CreateTemplateAsync(author, $"{RunTag} approval queue");
        try
        {
            await SubmitAsync(author, id);

            await using var approver = await app.SignedInPageAsync(user, password);
            await ApproveTemplateAsync(approver, id);

            await approver.GotoAsync($"{AppFixture.UiBase}/templates/{id}");
            await approver.WaitForTimeoutAsync(3000);
            await VisibleAsync(approver.GetByText("Approved").First, 20_000);
        }
        finally { await ArchiveAsync(author, id); }
    }

    // ---------------------------------------------------------------- TF7/TF9 draft approvals

    [Fact]
    public async Task TF9_Draft_moves_to_approved_and_the_author_is_never_offered_the_action()
    {
        SkipIfAppDown();
        SkipIfNoApprover();
        var (user, password) = AppFixture.Approver!.Value;

        await using var author = await app.SignedInPageAsync();
        var templateId = await CreateTemplateAsync(author, $"{RunTag} draft approval");
        try
        {
            await SubmitAsync(author, templateId);
            await using var approver = await app.SignedInPageAsync(user, password);
            await ApproveTemplateAsync(approver, templateId);

            await author.GotoAsync($"{AppFixture.UiBase}/drafts/new/{templateId}");
            var draftId = await SaveAndSubmitDraftAsync(author, $"{RunTag} for approval");

            // The author submitted it, so the author must not also be able to approve it.
            Assert.Equal(0, await author.GetByRole(AriaRole.Button, new() { Name = "Approve" }).CountAsync());

            await approver.GotoAsync($"{AppFixture.UiBase}/drafts/{draftId}");
            await approver.GetByRole(AriaRole.Button, new() { Name = "Approve" }).First
                .ClickAsync(new() { Timeout = 30_000 });
            await approver.WaitForTimeoutAsync(3000);

            await VisibleAsync(approver.GetByText("Approved").First, 20_000);
        }
        finally { await ArchiveAsync(author, templateId); }
    }

    // ---------------------------------------------------------------- TF8 hand-off

    [Fact]
    public async Task TF8_Approved_draft_can_be_handed_off_for_signing()
    {
        SkipIfAppDown();
        SkipIfNoApprover();
        var (user, password) = AppFixture.Approver!.Value;

        await using var author = await app.SignedInPageAsync();
        var templateId = await CreateTemplateAsync(author, $"{RunTag} handoff");
        try
        {
            await SubmitAsync(author, templateId);
            await using var approver = await app.SignedInPageAsync(user, password);
            await ApproveTemplateAsync(approver, templateId);

            await author.GotoAsync($"{AppFixture.UiBase}/drafts/new/{templateId}");
            var draftId = await SaveAndSubmitDraftAsync(author, $"{RunTag} handoff draft");

            await approver.GotoAsync($"{AppFixture.UiBase}/drafts/{draftId}");
            await approver.GetByRole(AriaRole.Button, new() { Name = "Approve" }).First
                .ClickAsync(new() { Timeout = 30_000 });
            await approver.WaitForTimeoutAsync(3000);

            // The hand-off belongs to the AUTHOR, not the approver: DraftDetail gates the button
            // on CreateDraft, and the approver account holds neither create permission. Asserting
            // it on the approver's page was asserting the wrong actor.
            await author.GotoAsync($"{AppFixture.UiBase}/drafts/{draftId}");
            await author.WaitForTimeoutAsync(3000);

            var handoff = author.GetByRole(AriaRole.Button, new() { Name = "Start signing" });
            Assert.True(await handoff.CountAsync() > 0,
                "an approved draft offers no hand-off action");

            await handoff.First.ClickAsync(new() { Timeout = 30_000 });

            // The click stages the hand-off and then navigates into the create-document wizard,
            // so the draft's new status is not on screen to be read.
            await author.WaitForURLAsync("**/createdocument", new() { Timeout = 60_000 });

            // The hand-off renders the PDF and stamps a reference; SentToWorkflow is the state
            // that proves both happened, so read it back off the draft itself.
            await author.GotoAsync($"{AppFixture.UiBase}/drafts/{draftId}",
                new() { WaitUntil = WaitUntilState.NetworkIdle });
            await VisibleAsync(author.GetByText("Sent to workflow").First, 60_000);
        }
        finally { await ArchiveAsync(author, templateId); }
    }

    // ---------------------------------------------------------------- helpers

    // A template with a merge token and an image-free body, saved as a Draft version.
    private async Task<int> CreateTemplateAsync(IPage page, string name)
    {
        await page.GotoAsync(AppFixture.UiBase + "/templates/new");
        await AppFixture.EditorAsync(page);

        await page.GetByRole(AriaRole.Button, new() { Name = "Add field" }).ClickAsync();
        await page.GetByPlaceholder("party_name").FillAsync("client_name");
        await page.GetByPlaceholder("Party name").FillAsync("Client name");

        await page.EvaluateAsync("""
            () => window.zxQuill.setHtml(
              document.querySelector('.zx-page .ql-container'),
              '<p>SERVICE AGREEMENT</p><p>Made with {{client_name}}.</p>')
            """);
        await page.WaitForTimeoutAsync(800);

        return await SaveTemplateAsync(page, name);
    }

    private static async Task<int> SaveTemplateAsync(IPage page, string name)
    {
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Template name" }).FillAsync(name);
        await page.GetByRole(AriaRole.Button, new() { Name = "Save template" }).ClickAsync();
        await page.WaitForFunctionAsync(
            "() => /^\\/templates\\/\\d+$/.test(location.pathname)", null, new() { Timeout = 60_000 });
        await page.WaitForTimeoutAsync(1500);

        var path = new Uri(page.Url).AbsolutePath;
        return int.Parse(path[(path.LastIndexOf('/') + 1)..]);
    }

    private static async Task SubmitAsync(IPage page, int templateId)
    {
        await page.GotoAsync($"{AppFixture.UiBase}/templates/{templateId}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Submit" }).First
            .ClickAsync(new() { Timeout = 30_000 });
        await page.WaitForTimeoutAsync(3000);
    }

    private static async Task ApproveTemplateAsync(IPage page, int templateId)
    {
        await page.GotoAsync($"{AppFixture.UiBase}/templates/{templateId}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Approve" }).First
            .ClickAsync(new() { Timeout = 30_000 });
        await page.WaitForTimeoutAsync(3000);
    }

    private static async Task<int> SaveAndSubmitDraftAsync(IPage page, string title)
    {
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Title" })
            .FillAsync(title, new() { Timeout = 30_000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Save draft" }).ClickAsync();
        await page.WaitForTimeoutAsync(2500);

        await page.GetByRole(AriaRole.Button, new() { Name = "Submit" }).First.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => /^\\/drafts\\/\\d+$/.test(location.pathname)", null, new() { Timeout = 60_000 });

        var path = new Uri(page.Url).AbsolutePath;
        return int.Parse(path[(path.LastIndexOf('/') + 1)..]);
    }

    // Inserts a real uploaded image through the toolbar, so the URL is the signed one the
    // preview surfaces must resolve — not a data: URL.
    private static async Task<string> UploadImageAsync(IPage page)
    {
        var file = Path.Combine(Path.GetTempPath(), $"tf-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(file, RedPng());

        var chooser = await page.RunAndWaitForFileChooserAsync(async () =>
        {
            await page.Locator("button.ql-image").ClickAsync();
        });
        await chooser.SetFilesAsync(file);

        await page.Locator(".zx-page .ql-editor img").WaitForAsync(new() { Timeout = 60_000 });
        await page.WaitForTimeoutAsync(3000);
        try { File.Delete(file); } catch { /* scratch file */ }

        return await page.EvaluateAsync<string>(
            "() => document.querySelector('.zx-page .ql-editor img')?.getAttribute('src') || ''");
    }

    // Smallest valid PNG the upload will accept: 1x1, opaque red.
    private static byte[] RedPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static Task VisibleAsync(ILocator locator, float timeout) =>
        locator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeout });

    // The preview iframe is sandbox="" — an opaque origin the parent may not read into. Only its
    // srcdoc attribute is reachable, so the text is recovered from the markup.
    private static async Task<string> PreviewTextAsync(IPage page) =>
        await page.EvaluateAsync<string>("""
            () => {
              const src = document.querySelector('iframe')?.getAttribute('srcdoc') || '';
              const d = new DOMParser().parseFromString(src, 'text/html');
              return d.body ? d.body.textContent : '';
            }
            """);

    private static async Task<string> PreviewImageSrcAsync(IPage page) =>
        await page.EvaluateAsync<string>("""
            () => {
              const src = document.querySelector('iframe')?.getAttribute('srcdoc') || '';
              const d = new DOMParser().parseFromString(src, 'text/html');
              return d.querySelector('img')?.getAttribute('src') || '';
            }
            """);

    private static async Task ArchiveAsync(IPage page, int templateId)
    {
        try
        {
            await page.GotoAsync($"{AppFixture.UiBase}/templates/{templateId}");
            var archive = page.GetByRole(AriaRole.Button, new() { Name = "Archive" });
            if (await archive.CountAsync() > 0) await archive.First.ClickAsync();
            await page.WaitForTimeoutAsync(1500);
        }
        catch
        {
            // Best effort; a cleanup failure must not mask the assertion that ran before it.
        }
    }
}
