using System;
using Microsoft.AspNetCore.Components;

namespace zxadocsui.Srevices;

public class SideDialogService
{
    TaskCompletionSource<object?>? _tcs;
    public event Action<Type, Dictionary<string, object>?, string?, int?>? OnShow;
    public event Action? OnClose;

    public async Task<T?> Show<TComponent, T>(Dictionary<string, object>? parameters = null, string? title = null, int? width = 420) where TComponent : IComponent
    {
        _tcs = new TaskCompletionSource<object?>();

        OnShow?.Invoke(typeof(TComponent), parameters, title, width);

        var result = await _tcs.Task;

        return (T?)result;
    }

    public void Close(object? result = null)
    {
        _tcs?.SetResult(result);
        OnClose?.Invoke();
    }
}