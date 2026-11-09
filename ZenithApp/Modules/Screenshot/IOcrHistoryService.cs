namespace ZenithApp.Modules.Screenshot;

public interface IOcrHistoryService
{
    event Action? HistoryChanged;

    void Add(string text);

    IReadOnlyList<string> GetHistory();

    void Clear();
}
