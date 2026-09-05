using System;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace zxadocsui.Srevices;

public class SideDialogService
{
    TaskCompletionSource<object?>? _tcs;
    public event Action<Type, Dictionary<string, object>?, string?, int?, Anchor?>? OnShow;
    public event Action? OnClose;

    public async Task<T?> Show<TComponent, T>(Dictionary<string, object>? parameters = null, string? title = null, int? width = 420, Anchor anchor = Anchor.Right) where TComponent : IComponent
    {
        _tcs = new TaskCompletionSource<object?>();

        OnShow?.Invoke(typeof(TComponent), parameters, title, width, anchor);

        var result = await _tcs.Task;

        return (T?)result;
    }

    public void Close(object? result = null)
    {
        // TrySetResult: closing twice (save, then the X) must not throw.
        _tcs?.TrySetResult(result);
        OnClose?.Invoke();
    }
}