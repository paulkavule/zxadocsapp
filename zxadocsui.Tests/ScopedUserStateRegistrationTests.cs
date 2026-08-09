using System.Text.RegularExpressions;
using Xunit;

namespace zxadocsui.Tests;

// Blazor Server scoped services live for the whole SignalR circuit, so a logout/login on the
// same circuit leaves every cache holding the PREVIOUS user's data — the menu keeps showing
// their items until the browser is refreshed (a refresh builds a new circuit, which is why the
// symptom looks like it fixes itself).
//
// UserSession.ResetUserState() clears everything registered as IScopedUserState, which makes
// the REGISTRATION the single point of failure: implement the interface, forget the line in
// Program.cs, and the leak returns silently with nothing to notice at compile time.
//
// Source-text based, like TailwindDeadClassTests — this project intentionally does not
// reference zxadocsui, so there is no assembly to reflect over.
[Trait("Category", "Fast")]
public class ScopedUserStateRegistrationTests
{
    // "public class AppState : IScopedUserState" / "... : IPermissionClientService, IScopedUserState"
    private static readonly Regex Implementation = new(
        @"class\s+(?<name>\w+)\s*:\s*[^{]*\bIScopedUserState\b", RegexOptions.Compiled);

    [Fact]
    public void Every_IScopedUserState_implementation_is_registered_in_Program()
    {
        var program = File.ReadAllText(Paths.Ui("Program.cs"));
        var implementations = FindImplementations().ToList();

        // If this ever hits zero the regex has drifted and the guard is silently passing.
        Assert.NotEmpty(implementations);

        var unregistered = implementations
            .Where(name => !program.Contains($"GetRequiredService<{name}>()")
                        && !program.Contains($"AddScoped<IScopedUserState, {name}>"))
            .ToList();

        Assert.True(unregistered.Count == 0,
            "These types implement IScopedUserState but are not wired into the IScopedUserState " +
            "registrations in Program.cs, so UserSession.ResetUserState() will not clear them and " +
            "their data leaks to the next user who signs in on the same circuit:\n  " +
            string.Join("\n  ", unregistered));
    }

    [Fact]
    public void The_permission_cache_participates()
    {
        // The reported bug: PermissionClientService cached the signed-in user's permissions for
        // the circuit's lifetime, so the sidebar kept the previous user's menu items.
        Assert.Contains("PermissionClientService", FindImplementations());
    }

    [Fact]
    public void Sign_out_and_sign_in_both_reset()
    {
        var session = File.ReadAllText(Paths.Ui("State", "UserSession.cs"));
        Assert.Contains("ResetUserState", session);

        // SignOut must clear rather than only emptying its own Items dictionary.
        var signOut = session[session.IndexOf("public async Task SignOut()", StringComparison.Ordinal)..];
        Assert.Contains("ResetUserState()", signOut);

        // An expired session lands on the login page without SignOut ever running, so the login
        // path has to reset too — before it stores the new token, or it would wipe it.
        var login = File.ReadAllText(Paths.Ui("Components", "Pages", "Login.razor.cs"));
        var resetAt = login.IndexOf("ResetUserState()", StringComparison.Ordinal);
        var tokenAt = login.IndexOf("SessionVariables.TOKEN", StringComparison.Ordinal);
        Assert.True(resetAt > 0, "Login must reset the previous user's state.");
        Assert.True(resetAt < tokenAt,
            "Login must reset BEFORE storing the new token, otherwise the reset clears it.");
    }

    private static IEnumerable<string> FindImplementations()
    {
        foreach (var dir in new[] { Paths.Ui("State"), Paths.Fe("Services") })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                foreach (Match m in Implementation.Matches(File.ReadAllText(file)))
                    yield return m.Groups["name"].Value;
        }
    }
}
