using System.Text.RegularExpressions;

namespace zxadocsui.Tests;

// Tailwind output is COMMITTED (wwwroot/ctailwindcss.css) and there is no build step that
// regenerates it, so a class the build never generated is silently a no-op. This has cost three
// separate defects in the templates module:
//
//   h-[560px]        a preview host with no height at all — collapsed to 2px, taking every preview
//                    with it (arbitrary value)
//   min-h-[420px]    a placeholder with no minimum height (arbitrary value)
//   lg:grid-cols-2   the approvals pages have never been two-column at any width, so each pane was
//                    full width and the page-shaped preview grew to ~2120px, putting Approve and
//                    Reject off screen (responsive variant)
//
// Two shapes are therefore checked: arbitrary values and variant-prefixed utilities. Plain
// single-word utilities are not — those come from the standard set. (min-h-0 is a counter-example
// that is also absent, but enumerating the whole standard set is a different job; the two checked
// shapes are where this build's gaps actually are.)
//
// No browser needed, so this runs on every edit.
[Trait("Category", "Fast")]
public class TailwindDeadClassTests
{
    // `h-[560px]`, `min-h-[420px]` — a bracketed arbitrary value.
    private static readonly Regex ArbitraryClass = new(@"[a-z-]+\[[^\]\s""']+\]", RegexOptions.Compiled);

    // `lg:grid-cols-2`, `md:col-span-2` — a breakpoint or state prefix, which Tailwind only emits
    // for the exact combinations it saw when the committed CSS was generated.
    private static readonly Regex VariantClass =
        new(@"^[a-z-]+:[a-z0-9:\[\]./-]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void Every_generated_tailwind_class_in_the_module_exists_in_the_committed_css()
    {
        var css = File.ReadAllText(Paths.Wwwroot("ctailwindcss.css"));
        var known = KnownDead();
        var missing = new List<string>();

        foreach (var file in ModuleMarkupFiles())
        {
            foreach (var name in ClassNamesIn(File.ReadAllText(file)))
            {
                // Tailwind escapes the brackets, dots and the variant colon in generated selectors:
                // `lg:h-[560px]` is emitted as `.lg\:h-\[560px\]`, so missing the colon would look
                // up `.lg:h-\[560px\]` and fail a class that is actually present.
                var selector = "." + Regex.Replace(name, @"([\[\]\.\%\/\(\)#,:])", @"\$1");
                if (css.Contains(selector, StringComparison.Ordinal)) continue;

                var entry = $"{Path.GetFileName(file)}: {name}";
                if (!known.Contains(entry)) missing.Add(entry);
            }
        }

        Assert.True(missing.Count == 0,
            "These Tailwind classes are used in the templates/drafting module but are not in the " +
            "committed ctailwindcss.css, so they do nothing at runtime. Either regenerate the CSS or use a " +
            "plain utility / scoped CSS instead:\n  " + string.Join("\n  ", missing));
    }

    [Theory]
    [InlineData("h-[13579px]")]        // arbitrary value
    [InlineData("lg:grid-cols-97")]    // responsive variant
    public void Guard_actually_detects_a_missing_class(string invented)
    {
        // Proves the assertion above can fail — a guard that cannot fail is not a guard.
        var css = File.ReadAllText(Paths.Wwwroot("ctailwindcss.css"));

        var selector = "." + Regex.Replace(invented, @"([\[\]\.\%\/\(\)#,:])", @"\$1");

        Assert.DoesNotContain(selector, css);
        Assert.Contains(invented, ClassNamesIn($"<div class=\"flex {invented} rounded\">"));
    }

    // Recorded pre-existing dead classes; the guard fails only for anything not listed.
    private static HashSet<string> KnownDead()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "known-dead-tailwind-classes.txt");
        if (!File.Exists(file)) return new HashSet<string>();

        return File.ReadAllLines(file)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToHashSet();
    }

    private static IEnumerable<string> ClassNamesIn(string markup) =>
        // Both spellings: plain HTML uses class=, a Blazor component parameter uses Class= — and it
        // was a component parameter (PagePreview Class="h-[560px]") that silently collapsed a preview.
        Regex.Matches(markup, @"\b[Cc]lass\s*=\s*""([^""]*)""")
            .SelectMany(m => m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(c => c.TrimStart('!'))
            .Where(c => ArbitraryClass.IsMatch(c) || VariantClass.IsMatch(c))
            .Distinct();

    private static IEnumerable<string> ModuleMarkupFiles()
    {
        foreach (var dir in new[]
                 {
                     Paths.Ui("Components", "Pages", "Dashboard", "Templates"),
                     Paths.Ui("Components", "Pages", "Dashboard", "Drafts"),
                     Paths.Ui("Components", "Pages", "Dashboard", "Roles"),
                     Paths.Ui("Components", "Custom"),
                 })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*.razor", SearchOption.AllDirectories))
                yield return f;
        }
    }
}
