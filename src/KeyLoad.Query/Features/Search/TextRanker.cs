using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

internal sealed class TextRanker
{
    private const double Bm25FrequencyScale = 2.2;
    private const double Bm25LengthScale = 1.2;
    private const double Bm25LengthBase = 0.25;
    private const double Bm25LengthWeight = 0.75;
    private const double IdfSmoothing = 0.5;
    private readonly ReadExecutionBudget budget;
    private readonly Dictionary<string, int> lookup;
    private readonly string[] path;
    private readonly int[] frequency;
    private readonly List<TextCandidate> candidates = [];
    private int corpusCount;
    private long totalLength;

    public TextRanker(string query, string field, ReadExecutionBudget budget)
    {
        this.budget = budget;
        var terms = SearchTerms.Enumerate(query, budget).Distinct(StringComparer.Ordinal).ToArray();
        lookup = terms.Select((term, index) => (term, index))
            .ToDictionary(pair => pair.term, pair => pair.index, StringComparer.Ordinal);
        frequency = new int[terms.Length];
        path = terms.Length == 0 ? [] : JsonData.PathSegments(field);
    }

    public bool HasTerms => lookup.Count != 0;

    public void Visit(DocumentRecord document)
    {
        budget.Check();
        corpusCount++;
        using var json = JsonDocument.Parse(document.Json);
        var value = JsonData.Scalar(json.RootElement, path);
        var counts = new Dictionary<int, int>();
        var length = 0;
        if (value is string content)
        {
            foreach (var term in SearchTerms.Enumerate(content, budget))
            {
                length++;
                if (lookup.TryGetValue(term, out var index))
                {
                    counts[index] = counts.GetValueOrDefault(index) + 1;
                }
            }
        }
        foreach (var index in counts.Keys)
        {
            frequency[index]++;
        }
        totalLength += length;
        if (counts.Count != 0)
        {
            candidates.Add(new(document.Reference, length, counts));
        }
    }

    public SearchScore[] Rank()
    {
        if (corpusCount == 0)
        {
            return [];
        }
        var average = Math.Max(1, (double)totalLength / corpusCount);
        var ranked = new List<SearchScore>(candidates.Count);
        foreach (var candidate in candidates)
        {
            budget.Check();
            double score = 0;
            foreach (var index in candidate.Counts.Keys.Order())
            {
                var count = candidate.Counts[index];
                var idf = Math.Log(1 + (corpusCount - frequency[index] + IdfSmoothing)
                    / (frequency[index] + IdfSmoothing));
                score += idf * count * Bm25FrequencyScale
                    / (count + Bm25LengthScale * (Bm25LengthBase + Bm25LengthWeight * candidate.Length / average));
            }
            if (score > 0)
            {
                ranked.Add(new(candidate.Reference, score));
            }
        }
        return ranked.OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Reference.Id, StringComparer.Ordinal).ToArray();
    }

    private sealed record TextCandidate(EntityRef Reference, int Length, Dictionary<int, int> Counts);
}
