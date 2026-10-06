using System.Globalization;

namespace KeyLoad.Comparisons;

/// <summary>Native ingestion batch budgets for the unchanged comparison corpus.</summary>
public sealed partial class NativeComparisonExecutionOptions
{
    internal const int DefaultMongoSeedBatchSize = 256;
    internal const int DefaultKeyLoadDocumentSeedBatchSize = 100;
    internal const int DefaultRedisSeedBatchSize = 256;
    internal const int DefaultOpenSearchBulkBatchSize = 64;

    /// <summary>The maximum MongoDB records submitted in one seed insert.</summary>
    public int MongoSeedBatchSize { get; set; } = DefaultMongoSeedBatchSize;
    /// <summary>The maximum KeyLoad documents committed in one scaled seed request.</summary>
    public int KeyLoadDocumentSeedBatchSize { get; set; } = DefaultKeyLoadDocumentSeedBatchSize;
    /// <summary>The maximum Redis writes scheduled and joined in one scaled seed batch.</summary>
    public int RedisSeedBatchSize { get; set; } = DefaultRedisSeedBatchSize;
    /// <summary>The maximum OpenSearch documents submitted in one bulk seed request.</summary>
    public int OpenSearchBulkBatchSize { get; set; } = DefaultOpenSearchBulkBatchSize;

    private void RecordSeedEvidence(IDictionary<string, string> parameters)
    {
        parameters[nameof(MongoSeedBatchSize)] = MongoSeedBatchSize.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(KeyLoadDocumentSeedBatchSize)] = KeyLoadDocumentSeedBatchSize.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(RedisSeedBatchSize)] = RedisSeedBatchSize.ToString(CultureInfo.InvariantCulture);
        parameters[nameof(OpenSearchBulkBatchSize)] = OpenSearchBulkBatchSize.ToString(CultureInfo.InvariantCulture);
    }
}
