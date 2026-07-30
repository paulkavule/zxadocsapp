using System.Text.RegularExpressions;

namespace zxadocsui.Tests;

// PageGeometry's millimetre members are doubles, so writing one into CSS formats it with
// CurrentCulture: on a comma-decimal host (de-DE, fr-FR, most of the EU) `2.5cm` becomes `2,5cm`,
// an invalid declaration the browser drops. Program.cs configures no request localisation, so the
// culture is the host's.
//
// The editor's A4 sheet did exactly this with its padding, and the failure is invisible — the sheet
// simply loses its margin and the text column widens from 605px to the full 794px page, so every
// exported image width is measured against the wrong column.
//
// The fix is to consume the pre-formatted PageGeometry.MarginCss (invariant) instead of deriving a
// length locally, and this guard keeps it that way. No browser needed, so it runs on every edit.
[Trait("Category", "Fast")]
public class CultureSafeGeometryTests
{
    // A millimetre member reached through the class — the only culture-sensitive members it has.
    private static readonly Regex MillimetreMember =
        new(@"PageGeometry\.[A-Za-z]*Mm\b", RegexOptions.Compiled);

    [Fact]
    public void No_module_file_formats_a_millimetre_geometry_value_itself()
    {
        var offenders = ModuleSourceFiles()
            .SelectMany(file => Lines(file).Where(l => MillimetreMember.IsMatch(l.text))
                                           .Select(l => $"{Path.GetFileName(file)}:{l.number}  {l.text.Trim()}"))
            .ToList();

        Assert.True(offenders.Count == 0,
            "These lines derive a CSS length from a millimetre value, which formats with " +
            "CurrentCulture and emits `2,5cm` on a comma-decimal host. Use the pre-formatted " +
            "PageGeometry.MarginCss (or add an invariant member to PageGeometry) instead:\n  " +
            string.Join("\n  ", offenders));
    }

    [Fact]
    public void Guard_actually_detects_a_locally_derived_length()
    {
        // A guard that cannot fail is not a guard. This is the exact line the editor carried.
        const string offending = "$\"padding:{PageGeometry.MarginMm / 10:0.####}cm\"";

        Assert.Matches(MillimetreMember, offending);
        Assert.DoesNotMatch(MillimetreMember, "$\"padding:{PageGeometry.MarginCss}\"");
    }

    private static IEnumerable<(int number, string text)> Lines(string file) =>
        File.ReadAllLines(file).Select((text, i) => (i + 1, text));

    private static IEnumerable<string> ModuleSourceFiles()
    {
        foreach (var dir in new[]
                 {
                     Paths.Ui("Components", "Pages", "Dashboard", "Templates"),
                     Paths.Ui("Components", "Pages", "Dashboard", "Drafts"),
                     Paths.Ui("Components", "Custom"),
                 })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var pattern in new[] { "*.razor", "*.cs" })
                foreach (var f in Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories))
                    yield return f;
        }
    }
}
