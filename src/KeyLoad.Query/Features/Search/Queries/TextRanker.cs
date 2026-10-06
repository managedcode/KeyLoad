using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

internal sealed class TextRanker
{
    private const int EqualOrder = 0;
    private const int EmptyElementCount = 0;
    private const int AdjacentElementOffset = 1;
    private const int MinimumAverageDocumentLength = 1;
    private const int ZeroScore = 0;

    private const double Bm25FrequencyScale = 2.2;
    private const double Bm25LengthScale = 1.2;
    private const double Bm25LengthBase = 0.25;
    private const double Bm25LengthWeight = 0.75;
    private const double IdfSmoothing = 0.5;
    private readonly ReadExecutionBudget budget;
    private readonly int budgetCheckInterval;
    private readonly int maximumDocumentWords;
    private readonly int maximumWordCharacters;
    private ITextProjectionLease? projection;
    private readonly string[] terms;
    private readonly Dictionary<string, int> lookup;
    private readonly string[] path;
    private readonly int[] frequency;
    private readonly List<TextCandidate> candidates = [];
    private int corpusCount;
    private long totalLength;

    public TextRanker(string query, string field, ReadExecutionBudget budget, int budgetCheckInterval,
        int maximumDocumentWords, int maximumWordCharacters)
    {
        this.budget = budget;
        this.budgetCheckInterval = budgetCheckInterval;
        this.maximumDocumentWords = maximumDocumentWords;
        this.maximumWordCharacters = maximumWordCharacters;
        terms = SearchTerms.Enumerate(query, budget, budgetCheckInterval, maximumDocumentWords, maximumWordCharacters).Distinct(StringComparer.Ordinal).ToArray();
        lookup = terms.Select((term, index) => (term, index))
            .ToDictionary(pair => pair.term, pair => pair.index, StringComparer.Ordinal);
        frequency = new int[terms.Length];
        path = terms.Length == EqualOrder ? [] : JsonData.PathSegments(field);
    }

    public bool HasTerms => lookup.Count != EmptyElementCount;
    public IReadOnlyList<string> Terms => terms;

    public void AttachProjection(ITextProjectionLease lease)
        => projection = lease;

    public void Visit(DocumentRecord document)
    {
        budget.Check();
        corpusCount++;
        projection?.BeginRecord(document.Reference, document.Revision);
        using var json = JsonDocument.Parse(document.Json);
        var value = JsonData.Scalar(json.RootElement, path);
        var counts = new Dictionary<int, int>();
        var length = EmptyElementCount;
        if (value is string content)
        {
            foreach (var term in SearchTerms.Enumerate(content, budget, budgetCheckInterval, maximumDocumentWords, maximumWordCharacters))
            {
                projection?.ObserveToken(term);
                length++;
                if (lookup.TryGetValue(term, out var index))
                {
                    counts[index] = counts.GetValueOrDefault(index) + AdjacentElementOffset;
                }
            }
        }
        foreach (var index in counts.Keys)
        {
            frequency[index]++;
        }
        totalLength += length;
        if (counts.Count != EmptyElementCount)
        {
            candidates.Add(new(document.Reference, length, counts));
        }
    }

    public SearchScore[] Rank()
    {
        if (corpusCount == EmptyElementCount)
        {
            return [];
        }
        var average = Math.Max(MinimumAverageDocumentLength, (double)totalLength / corpusCount);
        var ranked = new List<SearchScore>(candidates.Count);
        foreach (var candidate in candidates)
        {
            budget.Check();
            double score = ZeroScore;
            foreach (var index in candidate.Counts.Keys.Order())
            {
                var count = candidate.Counts[index];
                var idf = Math.Log(AdjacentElementOffset + (corpusCount - frequency[index] + IdfSmoothing)
                    / (frequency[index] + IdfSmoothing));
                score += idf * count * Bm25FrequencyScale
                    / (count + Bm25LengthScale * (Bm25LengthBase + Bm25LengthWeight * candidate.Length / average));
            }
            if (score > ZeroScore)
            {
                ranked.Add(new(candidate.Reference, score));
            }
        }
        return ranked.OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Reference.Id, StringComparer.Ordinal).ToArray();
    }

    private sealed record TextCandidate(EntityRef Reference, int Length, Dictionary<int, int> Counts);
}
