using zxadocsfe.Services;

namespace zxadocsui.State;

/// <summary>
/// Which organisation a system user is looking at (ZD-131). Scoped per circuit, never singleton:
/// a singleton would leak one system user's selection into every other session, which for a
/// cross-tenant role means showing them the wrong customer's data.
///
/// Implements <see cref="IScopedUserState"/> so a sign-out clears it rather than carrying the
/// previous user's selection into the next session on the same circuit.
/// </summary>
public class ActingOrganisationState : IActingOrganisation, IScopedUserState
{
    /// <summary>The header value for a read spanning every organisation.</summary>
    public const string All = "all";

    /// <summary>Fires when the selection changes so the open screen can reload.</summary>
    public event Func<Task>? Changed;

    /// <summary>Null when the caller is on their own organisation.</summary>
    public int? OrganisationId { get; private set; }

    public bool IsAllOrganisations { get; private set; }

    public string Label { get; private set; } = string.Empty;

    private bool suppressed;

    public string? HeaderValue => suppressed
        ? null
        : IsAllOrganisations
            ? All
            : OrganisationId is > 0 ? OrganisationId.Value.ToString() : null;

    /// <summary>
    /// Omits the header for calls made inside the scope, for the few reads that are about the
    /// system organisation itself rather than whichever tenant is selected. Does not fire Changed:
    /// the selection has not changed, so no other screen should reload.
    /// </summary>
    public IDisposable Suppressed() => new Suppression(this);

    private sealed class Suppression : IDisposable
    {
        private readonly ActingOrganisationState state;
        private readonly bool previous;

        public Suppression(ActingOrganisationState state)
        {
            this.state = state;
            previous = state.suppressed;
            state.suppressed = true;
        }

        public void Dispose() => state.suppressed = previous;
    }

    public async Task Select(int? organisationId, string label)
    {
        OrganisationId = organisationId;
        IsAllOrganisations = false;
        Label = label;
        await Notify();
    }

    public async Task SelectAll(string label)
    {
        OrganisationId = null;
        IsAllOrganisations = true;
        Label = label;
        await Notify();
    }

    public void ClearUserState()
    {
        OrganisationId = null;
        IsAllOrganisations = false;
        Label = string.Empty;
    }

    private async Task Notify()
    {
        if (Changed is not null)
            await Changed.Invoke();
    }
}
