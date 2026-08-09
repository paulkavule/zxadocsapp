using System;
using zxadocsfe.Services;
using static zxadocsfe.Helpers.AppConstants;

namespace zxadocsui.State;

public class RequestsContext : IScopedUserState
{
    public string? Token { get; set; }
    public string? RefreshToken { get; set; }
    public string? TenantId { get; set; }
    public bool SkipAuthForNextCall { get; set; }
    public bool IsBusy { get; set; }

    // Holds the previous user's bearer/refresh token and tenant otherwise.
    public void ClearUserState()
    {
        Token = null;
        RefreshToken = null;
        TenantId = null;
        SkipAuthForNextCall = false;
        IsBusy = false;
    }
}

public class AppState : IScopedUserState
{

    public int NavCount { set; get; } = 0;
    private readonly Dictionary<StateKey, object?> _values = new();

    public event Action? OnChange;

    private void NotifyStateChanged() => OnChange?.Invoke();

    public T? Get<T>(StateKey key, T? kvalue = default)
    {
        if (_values.TryGetValue(key, out var mvalue) && mvalue is T mtyped)
            return mtyped;

        return kvalue;
    }

    public void Set<T>(StateKey key, T value)
    {
        if (_values.TryGetValue(key, out var existing))
        {
            if (Equals(existing, value))
                return;

            _values[key] = value;
            NotifyStateChanged();
            return;
        }

        _values.Add(key, value);
        NotifyStateChanged();
    }

    public void ClearUserState()
    {
        if (_values.Count == 0 && NavCount == 0) return;
        _values.Clear();
        NavCount = 0;
        NotifyStateChanged();
    }
}
