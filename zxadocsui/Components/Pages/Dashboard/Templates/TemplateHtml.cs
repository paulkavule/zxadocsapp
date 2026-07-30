using System.Text.RegularExpressions;
using zxadocsfe.Services;
using zxadocslib.Helpers;

namespace zxadocsui.Components.Pages.Dashboard.Templates;

// =====================================================================================
// The parts of document handling that only the browser needs. The document itself — page
// geometry, the wrapper, the table and image rules, the stored Delta — is defined once in
// zxadocslib's DocumentHtml, so the renderer enforces exactly what authoring emits (ZD-90);
// the authoring pages call it directly rather than through this class.
// =====================================================================================

public static class TemplateHtml
{
    /// <summary>
    /// True when an editor body carries no visible text. Both authoring pages refuse to save an
    /// empty contract, and both were asking the same question with their own copy of this regex.
    /// </summary>
    public static bool IsBodyEmpty(string html) =>
        TagPattern.Replace(html ?? string.Empty, string.Empty).Trim().Length == 0;

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

    private static readonly Regex TagPattern = new("<[^>]+>", RegexOptions.Compiled);
}
