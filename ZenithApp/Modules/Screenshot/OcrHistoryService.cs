namespace ZenithApp.Modules.Screenshot;

public sealed class OcrHistoryService : IOcrHistoryService
{
    private const int MaxHistoryCount = 10;
    private readonly object _lock = new();
    private readonly List<string> _items = new();

    public event Action? HistoryChanged;

    public void Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var trimmed = text.Trim();

        lock (_lock)
        {
            // Не дублируем, если тот же текст уже на вершине
            if (_items.Count > 0 && string.Equals(_items[0], trimmed, StringComparison.Ordinal))
            {
                return;
            }

            // Удаляем предыдущие вхождения этого же текста
            _items.Remove(trimmed);

            // Вставляем наверх
            _items.Insert(0, trimmed);

            while (_items.Count > MaxHistoryCount)
            {
                _items.RemoveAt(_items.Count - 1);
            }
        }

        HistoryChanged?.Invoke();
    }

    public IReadOnlyList<string> GetHistory()
    {
        lock (_lock)
        {
            return _items.ToArray();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
        }

        HistoryChanged?.Invoke();
    }
}
