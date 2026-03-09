using System;
using static zxadocsfe.Helpers.AppConstants;

namespace zxadocsui.State;

public class RequestContext
{
    public string? Token { get; set; }
    public string? RefreshToken { get; set; }
    public string? TenantId { get; set; }
    public bool SkipAuthForNextCall { get; set; }
    public bool IsBusy { get; set; }
    public Dictionary<string, string> Claims { get; set; } = default!;
}

public class AppState
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
}