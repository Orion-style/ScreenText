namespace ZenithApp.Modules.T9Predictor;

public interface IWordDictionarySource : IDisposable
{
    string LanguageCode { get; }

    /// <summary>
    /// Searches for the most frequent word matching the prefix.
    /// Returns the matched word (in lowercase) or null if not found.
    /// </summary>
    string? FindBestMatch(string prefix);

    /// <summary>
    /// Returns top matches (ordered by frequency) up to maxResults.
    /// </summary>
    IReadOnlyList<string> FindMatches(string prefix, int maxResults);
}
