using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using zxadocslib.Helpers;

namespace zxadocsui.Tests;

// The checks that this module keeps breaking on. Every number is compared against PageGeometry
// rather than a literal, so changing the page moves the expectations with it.
//
// Test data: every template created here is named with the RunTag prefix and archived at the end,
// so a run leaves no active rows behind.
[Collection("app")]
[Trait("Category", "Ui")]
public class TemplateRenderTests(AppFixture app)
{
    private static readonly string RunTag = "[e2e-" + Guid.NewGuid().ToString("N")[..6] + "]";

    private void SkipIfAppDown()
    {
        Assert.SkipWhen(!app.AppReachable, app.SkipReason ?? "app not running");
    }

    // ---------------------------------------------------------------- editor geometry

    [Fact]
    public async Task Editor_sheet_is_an_A4_page_with_the_printable_column()
    {
        SkipIfAppDown();
        await using var page = await NewTemplatePageAsync();

        var m = await page.EvaluateAsync<JsonElement>("""
            () => {
              const sheet = document.querySelector('.zx-page');
              const ed = document.querySelector('.zx-page .ql-editor');
              const cs = getComputedStyle(ed), ss = getComputedStyle(sheet);
              const r = sheet.getBoundingClientRect();
              return { sheetW: Math.round(r.width), sheetH: Math.round(r.height),
                       boxSizing: ss.boxSizing, editorPadLeft: cs.paddingLeft,
                       column: Math.round(ed.clientWidth - parseFloat(cs.paddingLeft) - parseFloat(cs.paddingRight)) };
            }
            """);

        // The sheet must be the page itself, with the printed margin inside that width — without
        // border-box it came out 983px wide and the column 794px instead of 605px.
        Assert.Equal(PageGeometry.WidthPx, m.GetProperty("sheetW").GetInt32());
        Assert.Equal(PageGeometry.HeightPx, m.GetProperty("sheetH").GetInt32());
        Assert.Equal("border-box", m.GetProperty("boxSizing").GetString());
        Assert.Equal("0px", m.GetProperty("editorPadLeft").GetString());
        Assert.Equal(PageGeometry.ContentWidthPx, m.GetProperty("column").GetInt32());
    }

    [Fact]
    public async Task Editor_advertises_the_content_width_to_the_javascript()
    {
        SkipIfAppDown();
        await using var page = await NewTemplatePageAsync();

        var declared = await page.EvaluateAsync<string?>(
            "() => document.querySelector('.zx-page .ql-container').dataset.zxContentWidth");

        Assert.Equal(PageGeometry.ContentWidthPx.ToString(), declared);
    }

    // ---------------------------------------------------------------- images

    [Fact]
    public async Task Image_displays_and_is_capped_to_the_content_column_with_its_aspect_kept()
    {
        SkipIfAppDown();
        await using var page = await NewTemplatePageAsync();
        await SeedImageAsync(page);

        var m = await page.EvaluateAsync<JsonElement>("""
            () => {
              const ed = document.querySelector('.zx-page .ql-container');
              const q = window.Quill.find(ed), img = q.root.querySelector('img');
              const r = img.getBoundingClientRect(), cs = getComputedStyle(q.root);
              const column = q.root.clientWidth - parseFloat(cs.paddingLeft) - parseFloat(cs.paddingRight);
              const exported = window.zxQuill.getHtml(ed);
              const tag = (exported.match(/<img[^>]*>/) || [''])[0];
              return { loaded: img.complete && img.naturalWidth > 0,
                       naturalW: img.naturalWidth, naturalH: img.naturalHeight,
                       shownW: Math.round(r.width), column: Math.round(column),
                       aspect: +(r.width / r.height).toFixed(3),
                       hasWidthAttr: /\swidth="\d+"/.test(tag), hasHeightAttr: /\sheight="\d+"/.test(tag) };
            }
            """);

        // A 0-width image means the URL failed to load, which is what a broken signed URL looks
        // like — the symptom that also disables resizing and alignment.
        Assert.True(m.GetProperty("loaded").GetBoolean(),
            "the image did not load; a signed image URL that fails also kills resize and alignment");
        Assert.True(m.GetProperty("shownW").GetInt32() <= m.GetProperty("column").GetInt32());

        var native = m.GetProperty("naturalW").GetDouble() / m.GetProperty("naturalH").GetDouble();
        Assert.Equal(Math.Round(native, 2), Math.Round(m.GetProperty("aspect").GetDouble(), 2));

        // LibreOffice ignores CSS sizing and a lone width attribute; both attributes are required.
        Assert.True(m.GetProperty("hasWidthAttr").GetBoolean(), "exported <img> has no width attribute");
        Assert.True(m.GetProperty("hasHeightAttr").GetBoolean(), "exported <img> has no height attribute");
    }

    [Fact]
    public async Task Selecting_an_image_puts_resize_handles_exactly_on_it()
    {
        SkipIfAppDown();
        await using var page = await NewTemplatePageAsync();
        await SeedImageAsync(page);

        var box = await ClickImageAsync(page);

        var overlay = page.Locator(".ql-resize-overlay");
        await overlay.WaitForAsync(new() { Timeout = 15_000 });
        var o = await overlay.BoundingBoxAsync();

        // Handles sit on the overlay, so an overlay offset from the image is an image that cannot
        // be grabbed. Allow a pixel of rounding.
        Assert.True(Math.Abs(o!.X - box.X) <= 1.5 && Math.Abs(o.Y - box.Y) <= 1.5,
            $"overlay at ({o.X},{o.Y}) is not on the image at ({box.X},{box.Y})");
        Assert.True(Math.Abs(o.Width - box.Width) <= 1.5 && Math.Abs(o.Height - box.Height) <= 1.5,
            $"overlay is {o.Width}x{o.Height} but the image is {box.Width}x{box.Height}");

        // The alignment toolbar rides with the overlay; without it there is no way to centre.
        Assert.True(await page.Locator(".ql-resize-toolbar button").CountAsync() > 0,
            "no alignment buttons appeared, so the image cannot be positioned");
    }

    [Fact]
    public async Task Centring_an_image_is_recorded_in_a_form_the_stored_document_can_reproduce()
    {
        SkipIfAppDown();
        await using var page = await NewTemplatePageAsync();
        await SeedImageAsync(page);

        await ClickImageAsync(page);
        await page.Locator(".ql-resize-toolbar button").Nth(1).ClickAsync();   // left, centre, right...

        var applied = await page.EvaluateAsync<string>(
            "() => document.querySelector('.zx-page .ql-editor img').parentElement.className");
        Assert.Contains("ql-resize-style-center", applied);

        // The class is only in the editor's own stylesheet, so the stored document has to declare
        // it too or the image renders left-aligned in every preview and in the PDF.
        Assert.Contains(".ql-resize-style-center", DocumentHtml.Wrap("t", "<p>x</p>"));
    }

    // ---------------------------------------------------------------- tables

    [Fact]
    public async Task Table_survives_being_saved_and_reopened_for_a_new_version()
    {
        SkipIfAppDown();
        await using var page = await NewTemplatePageAsync();

        await SeedTableAsync(page, rows: 4, cols: 2);
        var authored = await TableShapeAsync(page);

        var templateId = await SaveTemplateAsync(page, RunTag + " table round trip");
        await page.GetByRole(AriaRole.Button, new() { Name = "New version" }).ClickAsync();
        await AppFixture.EditorAsync(page);
        await page.WaitForTimeoutAsync(2500);          // the Delta is applied after the editor mounts

        var reopened = await TableShapeAsync(page);

        // This is the defect that made template tables write-once: the rows came back empty.
        Assert.Equal(authored.rows, reopened.rows);
        Assert.Equal(authored.cells, reopened.cells);
        Assert.Equal(authored.text, reopened.text);
        Assert.True(reopened.rows > 0, "the table came back with no rows");

        await ArchiveAsync(page, templateId);
    }

    [Fact]
    public async Task Exported_table_carries_the_column_ratios_the_screen_shows()
    {
        SkipIfAppDown();
        await using var page = await NewTemplatePageAsync();
        await SeedTableAsync(page, rows: 3, cols: 2);

        var m = await page.EvaluateAsync<JsonElement>("""
            () => {
              const ed = document.querySelector('.zx-page .ql-container');
              const q = window.Quill.find(ed);
              const row = q.root.querySelector('tr');
              const shown = [...row.children].map(c => c.getBoundingClientRect().width);
              const total = shown.reduce((a, b) => a + b, 0);
              const exported = window.zxQuill.getHtml(ed);
              const firstRow = (exported.match(/<tr>[\s\S]*?<\/tr>/) || [''])[0];
              return { shownPct: shown.map(w => +(w / total * 100).toFixed(2)),
                       widths: [...firstRow.matchAll(/width="([\d.]+)%"/g)].map(x => +x[1]),
                       hasColgroup: /<colgroup/i.test(exported) };
            }
            """);

        var shown = m.GetProperty("shownPct").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        var stamped = m.GetProperty("widths").EnumerateArray().Select(x => x.GetDouble()).ToArray();

        // Without these, LibreOffice apportions columns by content and printed 28.7/71.3 where the
        // screen showed 50/50. A percentage colgroup is NOT the fix: it is only partly honoured and
        // overrides the cells.
        Assert.Equal(shown.Length, stamped.Length);
        for (var i = 0; i < shown.Length; i++)
            Assert.True(Math.Abs(shown[i] - stamped[i]) <= 0.5,
                $"column {i}: screen {shown[i]}% but exported {stamped[i]}%");
        Assert.False(m.GetProperty("hasColgroup").GetBoolean(),
            "a percentage colgroup overrides the cell widths in LibreOffice; do not emit one");
    }

    // ---------------------------------------------------------------- previews

    // One template walked through every surface that previews it, in the order the workflow visits
    // them. It is a single test rather than a theory per route because the surfaces are not
    // independently reachable: the approvals queue needs a SUBMITTED template, and the draft screens
    // need an APPROVED one, so each state is produced by acting on the one before it.
    //
    // Approving is a second actor's job — see AppFixture.Approver. Without those credentials the
    // first surface is still asserted and the rest skip, which is why the assertion order matters.
    [Fact]
    public async Task Preview_shows_a_whole_A4_page_that_fits_its_pane_on_every_surface()
    {
        SkipIfAppDown();
        await using var author = await NewTemplatePageAsync();
        await SeedTableAsync(author, rows: 2, cols: 2);
        var templateId = await SaveTemplateAsync(author, RunTag + " preview surfaces");

        try
        {
            // 1. Template detail.
            await author.GotoAsync($"{AppFixture.UiBase}/templates/{templateId}");
            await AssertPreviewFitsAsync(author, "template detail");

            await SubmitAsync(author, templateId);

            var approver = AppFixture.Approver;
            Assert.SkipWhen(approver is null,
                "set ZXADOCS_APPROVER and ZXADOCS_APPROVER_PASSWORD to a user holding " +
                "ApproveTemplate/ApproveDraft; the dev-default login holds neither, so the " +
                "approvals queue and both draft surfaces cannot be reached.");

            await using var reviewer = await app.SignedInPageAsync(approver!.Value.User, approver.Value.Password);

            // 2. Template approvals queue. The pane here has no height of its own, so an unbounded
            // page-shaped preview grew to ~1090px and pushed Approve/Reject below the fold.
            await reviewer.GotoAsync($"{AppFixture.UiBase}/templates/approvals");
            await reviewer.GetByText(RunTag + " preview surfaces").First.ClickAsync(new() { Timeout = 30_000 });
            await AssertPreviewFitsAsync(reviewer, "template approvals");
            await AssertActionsAreOnScreenAsync(reviewer, "Approve", "Reject");

            await reviewer.GetByRole(AriaRole.Button, new() { Name = "Approve" }).First.ClickAsync();
            await reviewer.WaitForTimeoutAsync(3000);

            // 3. Draft editor, and 4. draft detail, both authored by the first user.
            await author.GotoAsync($"{AppFixture.UiBase}/drafts/new/{templateId}");
            await AssertPreviewFitsAsync(author, "draft editor");

            var draftId = await SaveAndSubmitDraftAsync(author, RunTag + " preview draft");
            await author.GotoAsync($"{AppFixture.UiBase}/drafts/{draftId}");
            await AssertPreviewFitsAsync(author, "draft detail");
        }
        finally
        {
            await ArchiveAsync(author, templateId);
        }
    }

    // Every preview surface is the same component, so the expectations are identical; only the
    // pane around it differs, which is exactly what tends to break.
    private static async Task AssertPreviewFitsAsync(IPage page, string surface)
    {
        await page.Locator(".zx-preview").WaitForAsync(new() { Timeout = 30_000 });
        await page.WaitForTimeoutAsync(2500);

        var m = await page.EvaluateAsync<JsonElement>("""
            () => {
              const host = document.querySelector('.zx-preview'), sheet = document.querySelector('.zx-sheet');
              const h = host.getBoundingClientRect(), s = sheet.getBoundingClientRect();
              const cs = getComputedStyle(host);
              return { hostH: Math.round(h.height), sheetW: +s.width.toFixed(1), sheetH: +s.height.toFixed(1),
                       aspect: +(s.width / s.height).toFixed(3),
                       // The page dimensions arrive as custom properties on the host; if they are
                       // missing the sheet silently falls back to an iframe's default 150px.
                       pageW: cs.getPropertyValue('--zx-page-w').trim(),
                       pageH: cs.getPropertyValue('--zx-page-h').trim(),
                       fits: s.right <= h.right + 1 && s.bottom <= h.bottom + 1 &&
                             s.left >= h.left - 1 && s.top >= h.top - 1,
                       declaresPage: (document.querySelector('.zx-frame').getAttribute('srcdoc') || '')
                                       .includes('size:210mm 297mm') };
            }
            """);

        // The host collapsed to 2px once, taking every preview with it, because it relied on the
        // caller for a height.
        Assert.True(m.GetProperty("hostH").GetInt32() > 50, $"{surface}: the preview host has no height");

        var expected = Math.Round((double)PageGeometry.WidthPx / PageGeometry.HeightPx, 3);
        var aspect = m.GetProperty("aspect").GetDouble();
        Assert.True(Math.Abs(aspect - expected) < 0.005,
            $"{surface}: sheet is {m.GetProperty("sheetW").GetDouble():F1}x{m.GetProperty("sheetH").GetDouble():F1} " +
            $"(aspect {aspect:F3}), expected the page's {expected:F3}; host height " +
            $"{m.GetProperty("hostH").GetInt32()}px, --zx-page-w='{m.GetProperty("pageW").GetString()}' " +
            $"--zx-page-h='{m.GetProperty("pageH").GetString()}'");
        Assert.True(m.GetProperty("fits").GetBoolean(), $"{surface}: the page overflows its pane");
        Assert.True(m.GetProperty("declaresPage").GetBoolean(),
            $"{surface}: the previewed document does not declare an explicit A4 page");
    }

    // A preview the approver can see is useless if the buttons are below the fold.
    private static async Task AssertActionsAreOnScreenAsync(IPage page, params string[] names)
    {
        foreach (var name in names)
        {
            var box = await page.GetByRole(AriaRole.Button, new() { Name = name }).First.BoundingBoxAsync();
            Assert.NotNull(box);

            var viewportHeight = page.ViewportSize!.Height;
            Assert.True(box!.Y + box.Height <= viewportHeight,
                $"'{name}' sits at y={box.Y + box.Height:F0} in a {viewportHeight}px viewport — off screen");
        }
    }

    // ---------------------------------------------------------------- helpers

    private async Task<IPage> NewTemplatePageAsync()
    {
        var page = await app.SignedInPageAsync();
        await page.GotoAsync(AppFixture.UiBase + "/templates/new");
        await AppFixture.EditorAsync(page);
        return page;
    }

    // A real mouse click at the image's position, which is what an author does. It must NOT be an
    // element-targeted click: quill-resize-module injects `{pointer-events: none}` for its embed
    // tags so its own overlay handles receive events, which makes Playwright's actionability check
    // refuse the image. The click lands on the paragraph and the editor resolves it to the embed.
    private static async Task<LocatorBoundingBoxResult> ClickImageAsync(IPage page)
    {
        var image = page.Locator(".zx-page .ql-editor img");
        await image.ScrollIntoViewIfNeededAsync();
        var box = await image.BoundingBoxAsync()
                  ?? throw new InvalidOperationException("the image has no layout box");

        await page.Mouse.ClickAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        return box;
    }

    // Seeds an image in its own paragraph, the way authored content looks. A data URI keeps the
    // test independent of the image store and of the native file picker.
    private static async Task SeedImageAsync(IPage page)
    {
        await page.EvaluateAsync("""
            async () => {
              // 400x100 red PNG: an easy click target with an unambiguous 4.0 aspect ratio.
              const c = document.createElement('canvas'); c.width = 400; c.height = 100;
              const g = c.getContext('2d'); g.fillStyle = '#c00'; g.fillRect(0, 0, 400, 100);
              const ed = document.querySelector('.zx-page .ql-container');
              window.zxQuill.setHtml(ed, '<p>Body text.</p><p><img src="' + c.toDataURL('image/png') + '"></p><p>After.</p>');
            }
            """);
        await page.Locator(".zx-page .ql-editor img").WaitForAsync(new() { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(1000);
    }

    private static async Task SeedTableAsync(IPage page, int rows, int cols)
    {
        await page.EvaluateAsync($$"""
            async () => {
              const ed = document.querySelector('.zx-page .ql-container');
              const q = window.Quill.find(ed);
              window.zxQuill.setHtml(ed, '<h1>Render check</h1><p>Body.</p>');
              await new Promise(r => setTimeout(r, 500));
              q.setSelection(q.getLength() - 1, 0);
              q.getModule('table-better').insertTable({{rows}}, {{cols}});
              await new Promise(r => setTimeout(r, 900));
              q.root.querySelectorAll('td').forEach((c, i) => {
                const p = c.querySelector('p') || c; p.textContent = 'c' + i;
              });
            }
            """);
        await page.Locator(".zx-page .ql-editor table td").First.WaitForAsync(new() { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(600);
    }

    private static async Task<(int rows, int cells, string text)> TableShapeAsync(IPage page)
    {
        var m = await page.EvaluateAsync<JsonElement>("""
            () => {
              const q = window.Quill.find(document.querySelector('.zx-page .ql-container'));
              const t = q.root.querySelector('table');
              return { rows: t ? t.querySelectorAll('tr').length : 0,
                       cells: t ? t.querySelectorAll('td').length : 0,
                       text: t ? (t.innerText || '').replace(/\s+/g, '|') : '' };
            }
            """);
        return (m.GetProperty("rows").GetInt32(), m.GetProperty("cells").GetInt32(),
                m.GetProperty("text").GetString() ?? "");
    }

    private static async Task<int> SaveTemplateAsync(IPage page, string name)
    {
        // Target the labelled field: the dashboard chrome has its own search box, so the first
        // text input on the page is not the template name.
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Template name" }).FillAsync(name);
        await page.GetByRole(AriaRole.Button, new() { Name = "Save template" }).ClickAsync();

        // Blazor routes client-side, so there is no navigation Load event to wait for — poll the
        // path instead, and require a numeric id ("/templates/new" is where we already are).
        await page.WaitForFunctionAsync(
            "() => /^\\/templates\\/\\d+$/.test(location.pathname)",
            null, new() { Timeout = 60_000 });
        await page.WaitForTimeoutAsync(1500);

        var path = new Uri(page.Url).AbsolutePath;
        return int.Parse(path[(path.LastIndexOf('/') + 1)..]);
    }

    // Puts the template's version into PendingApproval, which is what the approvals queue lists.
    private static async Task SubmitAsync(IPage page, int templateId)
    {
        await page.GotoAsync($"{AppFixture.UiBase}/templates/{templateId}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Submit" }).First.ClickAsync(new() { Timeout = 30_000 });
        await page.WaitForTimeoutAsync(3000);
    }

    // Fills the title, saves, then submits — which is what navigates to /drafts/{id}.
    private static async Task<int> SaveAndSubmitDraftAsync(IPage page, string title)
    {
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Title" }).FillAsync(title);
        await page.GetByRole(AriaRole.Button, new() { Name = "Save draft" }).ClickAsync();
        await page.WaitForTimeoutAsync(2500);

        await page.GetByRole(AriaRole.Button, new() { Name = "Submit" }).First.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => /^\\/drafts\\/\\d+$/.test(location.pathname)",
            null, new() { Timeout = 60_000 });

        var path = new Uri(page.Url).AbsolutePath;
        return int.Parse(path[(path.LastIndexOf('/') + 1)..]);
    }

    // Leaves no active rows behind. There is no delete endpoint, so archive is the available verb.
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
            // Cleanup is best effort; a failure here must not mask the assertion that ran before it.
        }
    }
}
