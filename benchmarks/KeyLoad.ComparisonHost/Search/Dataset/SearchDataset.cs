using System.Globalization;

namespace KeyLoad.ComparisonHost.Search;

/// <summary>A text document of the standard full-text dataset; <paramref name="Text"/> is twelve space-separated words.</summary>
internal sealed record SearchDocument(string Id, string Text);

/// <summary>
/// The standard full-text dataset: each document is twelve words drawn deterministically from a 1,000-word
/// vocabulary <c>w000</c>..<c>w999</c>, so every word occurs in about 1.2% of documents.
/// </summary>
internal static class SearchDataset
{
    internal const int WordsPerDocument = 12;
    internal const int Vocabulary = 1000;
    private const string IdPrefix = "t";
    private const string IdFormat = "D9";
    private const string WordPrefix = "w";
    private const string WordFormat = "D3";
    private const char Space = ' ';
    private const ulong Mix = 0x9e3779b97f4a7c15ul;

    internal static string Id(int number) => IdPrefix + number.ToString(IdFormat, CultureInfo.InvariantCulture);

    internal static int Number(string id) => int.Parse(id.AsSpan(IdPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture);

    internal static SearchDocument Create(int number, int seed) => new(Id(number), string.Join(Space, Words(number, seed)));

    internal static string Word(int number, int position, int seed)
    {
        var state = unchecked(((ulong)number * Mix) ^ ((ulong)(position + 1) * 0xbf58476d1ce4e5b9ul) ^ (ulong)seed);
        state ^= state >> 31;
        state = unchecked(state * 0x94d049bb133111ebul);
        state ^= state >> 29;
        return WordPrefix + ((int)(state % Vocabulary)).ToString(WordFormat, CultureInfo.InvariantCulture);
    }

    internal static IEnumerable<string> Words(int number, int seed)
        => Enumerable.Range(0, WordsPerDocument).Select(position => Word(number, position, seed));

    /// <summary>Checks that the document contains every term.</summary>
    internal static bool Contains(string id, IReadOnlyList<string> terms, int seed)
    {
        var words = Words(Number(id), seed).ToHashSet(StringComparer.Ordinal);
        return terms.All(words.Contains);
    }
}
