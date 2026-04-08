namespace MpvShell.App.Services;

public sealed class RecentUrlStore
{
    private readonly List<string> _items = new();

    public IReadOnlyList<string> Items => _items;

    public void Add(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        _items.Remove(url);
        _items.Insert(0, url);

        if (_items.Count > 10)
        {
            _items.RemoveAt(_items.Count - 1);
        }
    }
}
