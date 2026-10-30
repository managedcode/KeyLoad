using System.Collections;
using System.Collections.Immutable;

namespace KeyLoad.Comparisons;

/// <summary>Generates model inputs lazily, retaining only the bounded query set.</summary>
internal sealed class BenchmarkSessionModelCorpus : IComparisonCorpus
{
    private const int QueryCount = 16;
    private readonly BenchmarkSessionModelSettings options;
    private readonly Lazy<string> digest;

    internal BenchmarkSessionModelCorpus(BenchmarkSessionOptions session)
    {
        options = new(session);
        Documents = new DocumentView(this);
        digest = new(() => BenchmarkCorpusHash.Compute(Documents, Edges));
    }

    public IComparisonSettings Settings => options;
    public IReadOnlyList<BenchmarkDocument> Documents { get; }
    public IReadOnlyList<BenchmarkEdge> Edges => ImmutableArray<BenchmarkEdge>.Empty;
    public int GraphVertexCount => BenchmarkSessionLimits.NoObservedItems;
    public string Sha256 => digest.Value;
    public BenchmarkDocument CreateDocument(int number) => BenchmarkDocumentFactory.Create(options, number);
    public BenchmarkDocument Input(Scenario scenario, int repetition, int operation)
        => CreateDocument(scenario is Scenario.QueueCycle or Scenario.StreamAppend
            ? checked(options.Documents + repetition * options.Operations + operation)
            : operation % options.Documents);

    internal BenchmarkDocument[] Queries() => [.. Enumerable.Range(BenchmarkSessionLimits.NoObservedItems,
        Math.Min(QueryCount, options.Documents)).Select(CreateDocument)];

    internal ImmutableArray<FoundDocument>[] ExactNeighbors(BenchmarkDocument[] queries, CancellationToken token)
    {
        var heaps = queries.Select(_ => new PriorityQueue<BenchmarkDocument, (double Score, int ReverseNumber)>()).ToArray();
        foreach (var document in Documents)
        {
            token.ThrowIfCancellationRequested();
            for (var index = BenchmarkSessionLimits.NoObservedItems; index < queries.Length; index++)
            {
                Retain(heaps[index], document, BenchmarkCosineOracle.Score(queries[index].Vector, document.Vector));
            }
        }
        return [.. heaps.Select(heap => heap.UnorderedItems.OrderByDescending(item => item.Priority.Score)
            .ThenBy(item => item.Element.Id, StringComparer.Ordinal)
            .Select(item => new FoundDocument(item.Element.Id, item.Element.Json)).ToImmutableArray())];
    }

    private void Retain(PriorityQueue<BenchmarkDocument, (double Score, int ReverseNumber)> heap, BenchmarkDocument document, double score)
    {
        var priority = (score, -document.Number);
        if (heap.Count < options.TopK)
        {
            heap.Enqueue(document, priority);
        }
        else if (heap.TryPeek(out _, out var worst) && priority.CompareTo(worst) > BenchmarkSessionLimits.NoObservedItems)
        {
            heap.DequeueEnqueue(document, priority);
        }
    }

    private sealed class DocumentView(BenchmarkSessionModelCorpus owner) : IReadOnlyList<BenchmarkDocument>
    {
        public int Count => owner.options.Documents;
        public BenchmarkDocument this[int index] => (uint)index < (uint)Count
            ? owner.CreateDocument(index) : throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<BenchmarkDocument> GetEnumerator()
        {
            for (var index = BenchmarkSessionLimits.NoObservedItems; index < Count; index++)
            {
                yield return this[index];
            }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
