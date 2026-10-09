using System.Collections;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace KeyLoad.Comparisons;

/// <summary>Lazy canonical 1024-byte native document corpus; no retained record/payload table.</summary>
public sealed class DocumentComparisonCorpus : IComparisonCorpus
{
    internal DocumentInitializationTiming InitializationTiming { get; } = new();
    private readonly int records;
    private readonly Lazy<string> digest;
    /// <summary>Creates a bounded corpus whose settings retain actual native client admission.</summary>
    public DocumentComparisonCorpus(int records, int clients)
    {
        if (records < DocumentMeasurementValues.NoObservedItems || records > DocumentComparisonContract.Current.Ingestion.Records || clients is < DocumentMeasurementValues.SingleItemCount or > DocumentMeasurementValues.MaximumClients)
        {
            throw new ArgumentOutOfRangeException(nameof(records));
        }

        this.records = records;
        Settings = new DocumentComparisonSettings(records, clients);
        Documents = new LazyDocuments(this);
        digest = new(() => DocumentComparisonOracle.Digest(Enumerable.Range(DocumentMeasurementValues.NoObservedItems, records).Select(CreateDocument)));
    }
    /// <inheritdoc/>
    public IComparisonSettings Settings { get; }
    /// <inheritdoc/>
    public IReadOnlyList<BenchmarkDocument> Documents { get; }
    /// <inheritdoc/>
    public IReadOnlyList<BenchmarkEdge> Edges => Array.Empty<BenchmarkEdge>();
    /// <inheritdoc/>
    public int GraphVertexCount => DocumentMeasurementValues.NoObservedItems;
    /// <inheritdoc/>
    public string Sha256 => digest.Value;
    /// <inheritdoc/>
    public BenchmarkDocument CreateDocument(int number)
    {
        if (number < DocumentMeasurementValues.NoObservedItems || number >= checked(DocumentComparisonContract.Current.Ingestion.Records + DocumentComparisonContract.Current.Operations))
        {
            throw new ArgumentOutOfRangeException(nameof(number));
        }

        var id = ScaledComparisonCorpus.Id(number);
        var empty = ScaledComparisonCorpus.EmptyJson(id, number);
        var padding = new string(DocumentProtocolText.PaddingCharacter, DocumentComparisonContract.Current.PayloadBytes - Encoding.UTF8.GetByteCount(empty));
        var json = JsonSerializer.Serialize(new { id, number, text = DocumentProtocolText.KeyLoadSharedCorpus, padding });
        return new(number, id, json, ImmutableArray<float>.Empty);
    }
    /// <summary>Creates a same-sized replacement whose complete canonical body differs from its initial value.</summary>
    public BenchmarkDocument Replacement(int number)
    {
        var original = CreateDocument(number);
        return original with { Json = original.Json.Replace(DocumentProtocolText.KeyLoadSharedCorpus, DocumentProtocolText.KeyLoadUpdateCorpus, StringComparison.Ordinal) };
    }
    /// <inheritdoc/>
    public BenchmarkDocument Input(Scenario scenario, int repetition, int operation, bool warmup) => CreateDocument(operation);
    private sealed class LazyDocuments(DocumentComparisonCorpus owner) : IReadOnlyList<BenchmarkDocument>
    {
        public int Count => owner.records;
        public BenchmarkDocument this[int index] => index >= DocumentMeasurementValues.NoObservedItems && index < Count ? owner.CreateDocument(index) : throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<BenchmarkDocument> GetEnumerator()
        {
            for (var index = DocumentMeasurementValues.NoObservedItems; index < Count; index++)
            {
                yield return this[index];
            }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

internal sealed record DocumentComparisonSettings(int Documents, int Concurrency) : IComparisonSettings
{
    public int Seed => DocumentComparisonContract.Current.Seed;
    public int Operations => DocumentComparisonContract.Current.Operations;
    public int Warmup => DocumentComparisonContract.Current.Warmup;
    public int Repetitions => DocumentComparisonContract.Current.Repetitions;
    public int PayloadBytes => DocumentComparisonContract.Current.PayloadBytes;
    public int Dimensions => DocumentMeasurementValues.DocumentSchemaDimensions;
    public int TopK => DocumentMeasurementValues.DefaultUnusedTopK;
    public int TimeoutSeconds => DocumentComparisonContract.Current.TimeoutSeconds;
    public int GraphVertices => DocumentMeasurementValues.NoObservedItems;
    public int GraphFanOut => DocumentMeasurementValues.NoObservedItems;
    public int GraphDepth => DocumentMeasurementValues.NoObservedItems;
}
