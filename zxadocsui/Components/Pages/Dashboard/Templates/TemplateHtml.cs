using System.Text.RegularExpressions;
using zxadocsfe.Services;

namespace zxadocsui.Components.Pages.Dashboard.Templates;

// =====================================================================================
// The one place that turns editor content into a stored/previewable document.
//
// Previously each page wrapped the body itself, and the copies drifted: neither emitted
// table CSS, so a table authored in the editor — whose borders come from
// quill-table-better's own stylesheet — rendered as nothing at all once outside the
// editor, in both the preview iframe and the generated PDF.
// =====================================================================================

public static class TemplateHtml
{
    // Page margins go on @page, NOT on body. LibreOffice's HTML import computes a table's
    // width:100% against a box that a body margin has already shrunk, collapsing every column
    // to a single character — verified by bisection: body{margin:2.5cm} alone reproduces it,
    // and moving the margin to @page fixes it while keeping the same A4 geometry.
    //
    // Borders must also be declared here rather than inherited from the editor's plugin CSS,
    // which exists only inside the authoring page; without them a table renders as nothing.
    // @page carries the printed margin; the screen-only padding gives the preview iframe the
    // same visual inset without reaching the PDF (LibreOffice ignores @media screen, measured
    // at 70.95pt ≈ 2.5cm left margin, i.e. no doubling).
    private const string Styles =
        "@page{margin:2.5cm}" +
        "@media screen{body{padding:2.5cm}}" +
        "body{font-family:'Liberation Serif',serif;font-size:12pt;line-height:1.5;margin:0;color:#111}" +
        "h1{font-size:18pt}h2{font-size:14pt}ul,ol{margin-left:1.2em}" +
        "img{max-width:100%;height:auto}" +
        "table{border-collapse:collapse;width:100%;margin:8pt 0}" +
        "td,th{border:1px solid #444;padding:6pt;vertical-align:top;height:1.6em}" +
        "th{background:#f2f2f2;font-weight:bold}";

    public static string Wrap(string title, string body) =>
        $"<!doctype html><html><head><meta charset=\"utf-8\"><title>{System.Net.WebUtility.HtmlEncode(title)}</title>" +
        $"<style>{Styles}</style></head><body>" + CleanBody(body) + "</body></html>";

    // quill-table-better parks a <temporary> element inside the table while editing. It is
    // an authoring artefact with no meaning in a stored contract, so it is dropped.
    private static readonly Regex TemporaryElement =
        new(@"<temporary\b[^>]*>.*?</temporary>|<temporary\b[^>]*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    // LibreOffice's HTML import ignores CSS borders on cells, so a table styled only by CSS
    // prints as invisible text columns. The legacy border/width attributes are honoured, and
    // browsers still prefer the stylesheet, so both surfaces render.
    private static readonly Regex TableTag = new(@"<table\b(?![^>]*\bborder=)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string CleanBody(string body)
    {
        var cleaned = TemporaryElement.Replace(body ?? string.Empty, string.Empty);
        return TableTag.Replace(cleaned, "<table border=\"1\" width=\"100%\"");
    }

    // Stored HTML holds canonical, host-relative image URLs. A preview iframe resolves those
    // against the UI's own origin and gets a 404, so they must be swapped for the API's
    // signed absolute URLs before display. Preview only — never saved.
    public static async Task<string> WithDisplayableImagesAsync(string html, ITemplateClientService api)
    {
        if (string.IsNullOrEmpty(html)) return html;

        var ids = ImageIdPattern.Matches(html).Select(m => m.Groups["id"].Value).Distinct().ToArray();
        if (ids.Length == 0) return html;

        var (ok, signed, _) = await api.SignImages(ids);
        if (!ok) return html;

        foreach (var image in signed)
            html = html.Replace($"/api/templates/images?id={image.Id}", image.DisplayUrl);

        return html;
    }

    private static readonly Regex ImageIdPattern =
        new(@"/api/templates/images\?id=(?<id>[A-Za-z0-9\-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
}
