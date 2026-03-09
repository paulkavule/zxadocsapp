using System;

namespace zxadocsui.State;

public interface IUserSession
{
    T GetItem<T>(string key);
    void AddItem<T>(string key, T value);
    void RemoveItem(string key);
}
public class UserSession : IUserSession
{
    private Dictionary<string, object> Items { get; set; } = new Dictionary<string, object>();

    public T GetItem<T>(string key)
    {
        try
        {
            return (T)Items[key];
        }
        catch //(Exception ex)
        {

            return default(T);
        }
    }
    public void AddItem<T>(string key, T value)
    {
        if (Items.Keys.Contains(key))
            Items[key] = value;
        else
            Items.Add(key, value);
    }

    public void RemoveItem(string key)
    {
        if (Items.Keys.Contains(key))
            Items.Remove(key);
    }

}

