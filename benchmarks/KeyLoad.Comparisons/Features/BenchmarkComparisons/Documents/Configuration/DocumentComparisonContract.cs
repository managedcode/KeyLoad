using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.Comparisons;

/// <summary>The embedded machine contract shared with the native job planner.</summary>
public sealed record DocumentComparisonContract
{
    private static readonly Lazy<DocumentComparisonContract> Contract = new(Read);
    /// <summary>Gets the strictly admitted canonical inventory.</summary>
    public static DocumentComparisonContract Current => Contract.Value;
    /// <summary>Gets the schema version.</summary>
    public required int SchemaVersion { get; init; }
    /// <summary>Gets the independently named measurement family.</summary>
    public required string Family { get; init; }
    /// <summary>Gets admitted actual native member counts.</summary>
    public required ImmutableArray<int> NodeCounts { get; init; }
    /// <summary>Gets actual dataset counts.</summary>
    public required ImmutableArray<int> DatasetSizes { get; init; }
    /// <summary>Gets the complete pure and mixed schedule names.</summary>
    public required ImmutableArray<string> Scenarios { get; init; }
    /// <summary>Gets configure-only ingestion settings.</summary>
    public required DocumentIngestionContract Ingestion { get; init; }
    /// <summary>Gets exact measured operations per ordinary repetition.</summary>
    public required int Operations { get; init; }
    /// <summary>Gets complete independently initialized repetitions.</summary>
    public required int Repetitions { get; init; }
    /// <summary>Gets actual ordinary clients.</summary>
    public required int Clients { get; init; }
    /// <summary>Gets excluded warmup operations.</summary>
    public required int Warmup { get; init; }
    /// <summary>Gets deterministic seed.</summary>
    public required int Seed { get; init; }
    /// <summary>Gets canonical UTF8 payload size.</summary>
    public required int PayloadBytes { get; init; }
    /// <summary>Gets original outstanding calls per client.</summary>
    public required int MaximumOutstandingPerClient { get; init; }
    /// <summary>Gets full-count histogram bounds.</summary>
    public required DocumentHistogramContract LatencyHistogram { get; init; }
    /// <summary>Gets the canonical native operation deadline.</summary>
    public required int TimeoutSeconds { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static DocumentComparisonContract Read()
    {
        using var stream = typeof(DocumentComparisonContract).Assembly.GetManifestResourceStream(DocumentProtocolText.KeyLoadComparisonsDocumentContract)
            ?? throw new InvalidDataException(DocumentProtocolText.DocumentContractMissing);
        var value = JsonSerializer.Deserialize<DocumentComparisonContract>(stream, JsonOptions)
            ?? throw new InvalidDataException(DocumentProtocolText.DocumentContractMissing);
        if (value.SchemaVersion != DocumentMeasurementValues.SingleItemCount || value.Family != DocumentProtocolText.DocumentV1 || !value.NodeCounts.SequenceEqual([DocumentMeasurementValues.SingleItemCount, DocumentMeasurementValues.CanonicalRepetitions])
            || !value.DatasetSizes.SequenceEqual([DocumentMeasurementValues.SmallDatasetRecords, DocumentMeasurementValues.IngestionRecords])
            || !value.Scenarios.SequenceEqual(Enum.GetNames<DocumentComparisonScenario>().Where(name => name != nameof(DocumentComparisonScenario.Ingest)))
            || value.Ingestion.Scenario != nameof(DocumentComparisonScenario.Ingest) || value.Ingestion.Records != DocumentMeasurementValues.IngestionRecords
            || !value.Ingestion.Clients.SequenceEqual([DocumentMeasurementValues.SingleItemCount, DocumentMeasurementValues.BaselineConcurrentClients, DocumentMeasurementValues.MaximumClients]) || value.Operations != DocumentMeasurementValues.SmallDatasetRecords || value.Repetitions != DocumentMeasurementValues.CanonicalRepetitions
            || value.Clients != DocumentMeasurementValues.OrdinaryClients || value.Warmup != DocumentMeasurementValues.CanonicalWarmup || value.Seed != DocumentMeasurementValues.CanonicalSeed || value.PayloadBytes != DocumentMeasurementValues.CanonicalPayloadBytes
            || value.MaximumOutstandingPerClient != DocumentMeasurementValues.SingleItemCount || value.TimeoutSeconds != DocumentMeasurementValues.OperationTimeoutSeconds
            || value.LatencyHistogram.ResolutionMicroseconds != DocumentMeasurementValues.HistogramResolutionMicroseconds || value.LatencyHistogram.MaximumMilliseconds != DocumentMeasurementValues.MaximumLatencyMilliseconds
            || value.LatencyHistogram.Overflow != DocumentProtocolText.FailQualification)
        {
            throw new InvalidDataException(DocumentProtocolText.DocumentContractInvalid);
        }

        return value;
    }
}

/// <summary>Canonical ingestion inventory.</summary>
public sealed record DocumentIngestionContract
{
    /// <summary>Gets the configure-only schedule name.</summary>
    public required string Scenario { get; init; }
    /// <summary>Gets exact total records across all clients.</summary>
    public required int Records { get; init; }
    /// <summary>Gets actual admitted client counts.</summary>
    public required ImmutableArray<int> Clients { get; init; }
}
/// <summary>Canonical bounded full-count latency histogram.</summary>
public sealed record DocumentHistogramContract
{
    /// <summary>Gets bucket resolution.</summary>
    public required int ResolutionMicroseconds { get; init; }
    /// <summary>Gets largest admitted measured latency.</summary>
    public required int MaximumMilliseconds { get; init; }
    /// <summary>Gets fail-qualification overflow policy.</summary>
    public required string Overflow { get; init; }
}
