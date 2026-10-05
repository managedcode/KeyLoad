using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

/// <summary>Runs one profile-bound vector workload against a real native vector target.</summary>
/// <param name="profile">The immutable vector profile.</param>
/// <param name="executionOptions">The explicitly configured native limits.</param>
public sealed class VectorComparisonRunner(VectorComparisonProfile profile, IOptions<NativeComparisonExecutionOptions> executionOptions)
{
    private readonly VectorComparisonProfile profile = profile ?? throw new ArgumentNullException(nameof(profile));
    private readonly IOptions<NativeComparisonExecutionOptions> executionOptions = ReadExecution(profile, executionOptions);

    /// <summary>Executes corpus verification, index construction, warmup and the measured vector workload.</summary>
    /// <param name="target">The real native target owned by the Aspire host.</param>
    /// <param name="sourceRevision">The exact source revision for provenance.</param>
    /// <param name="storage">The observed storage profile description.</param>
    /// <param name="cancellationToken">Cancels the complete cell.</param>
    /// <returns>A source-bound report containing native receipts and measured vector results.</returns>
    public async Task<ComparisonReport> RunAsync(IVectorComparisonTarget target, string? sourceRevision,
        string storage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!target.Supports(profile.IndexKind, profile.QueryMode))
        {
            throw new NotSupportedException($"{target.Name}{VectorComparisonRunnerValues.DoesNotImplement}{profile.IndexKind}{VectorComparisonRunnerValues.AlgorithmModeSeparator}{profile.QueryMode}{VectorComparisonRunnerValues.Natively}");
        }

        var started = TimeProvider.System.GetUtcNow();
        var corpus = new VectorComparisonCorpus(profile);
        var datasetHash = ComputeDatasetHash(corpus, cancellationToken);
        var loaded = await target.IngestAsync(StreamDocumentsAsync(corpus, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (loaded != profile.RecordCount)
        {
            throw new InvalidDataException($"{VectorComparisonRunnerValues.NativeIngestionLoaded}{loaded}{VectorComparisonRunnerValues.RecordsExpected}{profile.RecordCount}{VectorComparisonRunnerValues.SentencePeriod}");
        }

        await new VectorResultValidator(profile, executionOptions).ValidateReadbackAsync(target, corpus, cancellationToken).ConfigureAwait(false);

        var index = await target.BuildIndexAsync(profile, cancellationToken).ConfigureAwait(false);
        var parameters = new Dictionary<string, string>(index.Parameters, StringComparer.Ordinal);
        executionOptions.Value.RecordEvidence(parameters);
        index = index with { Parameters = parameters };
        var queries = corpus.CreateQueries();
        var expected = corpus.ExactNeighborsBatch(queries, cancellationToken);

        var plan = await target.ExplainAsync(queries[VectorComparisonRunnerValues.FirstIndex], profile.QueryMode, cancellationToken).ConfigureAwait(false);
        ValidateIndex(profile, index, plan);
        for (var i = VectorComparisonRunnerValues.FirstIndex; i < profile.WarmupQueries; i++)
        {
            await new VectorResultValidator(profile, executionOptions).ValidateQueryAsync(target, corpus, queries[i % queries.Count], expected[i % queries.Count], cancellationToken).ConfigureAwait(false);
        }

        var measured = await new VectorWorkloadExecutor(profile, executionOptions).RunAsync(target, corpus, queries, expected,
            cancellationToken).ConfigureAwait(false);
        return CreateReport(target, sourceRevision, storage, started, datasetHash, loaded, index, plan, measured);
    }

    private static IOptions<NativeComparisonExecutionOptions> ReadExecution(VectorComparisonProfile profile,
        IOptions<NativeComparisonExecutionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(options);
        var execution = options.Value;
        execution.Validate();
        if (execution.OperationTimeout.TotalSeconds != profile.TimeoutSeconds)
        {
            throw new ArgumentException(VectorComparisonRunnerValues.TheNativeOperationTimeoutMustEqual, nameof(options));
        }
        return options;
    }

    internal static void ValidateIndex(VectorComparisonProfile profile, VectorIndexReceipt index, string plan)
    {
        if (index.IndexKind != profile.IndexKind || !double.IsFinite(index.BuildMilliseconds) || index.BuildMilliseconds < VectorComparisonRunnerValues.FirstIndex
            || (profile.IndexKind == VectorIndexKind.Exact && index.BuildMilliseconds != VectorComparisonRunnerValues.FirstIndex)
            || (profile.IndexKind != VectorIndexKind.Exact && index.BuildMilliseconds <= VectorComparisonRunnerValues.FirstIndex))
        {
            throw new InvalidDataException(VectorComparisonRunnerValues.NativeIndexReceiptDoesNotMatch);
        }

        if (string.IsNullOrWhiteSpace(plan))
        {
            throw new InvalidDataException(VectorComparisonRunnerValues.NativeQueryPlanIsEmpty);
        }

        if (profile.IndexKind is VectorIndexKind.Hnsw or VectorIndexKind.IvfFlat
            && !plan.Contains(profile.IndexKind == VectorIndexKind.Hnsw ? VectorProfileTokens.HnswMethod : VectorProfileTokens.IvfFlatMethod, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(VectorComparisonRunnerValues.TheMeasuredPostgreSQLQueryPlanDoes);
        }

        if (profile.IndexKind == VectorIndexKind.NativeAnn && string.IsNullOrWhiteSpace(index.Definition))
        {
            throw new InvalidDataException(VectorComparisonRunnerValues.NativeANNDidNotProvideItsActual);
        }

    }

    private ComparisonReport CreateReport(IVectorComparisonTarget target, string? sourceRevision, string storage,
        DateTimeOffset started, string datasetHash, int loaded, VectorIndexReceipt index, string plan,
        VectorWorkloadObservations measured)
    {
        var sorted = measured.Latencies.Order().ToArray();
        var metrics = new VectorMetrics(profile.RecordCount, loaded, profile.MeasuredQueries, measured.QuerySuccesses,
            measured.UpdateAttempts, measured.UpdateSuccesses, measured.Recalls.Average(), measured.Recalls.Min(), measured.Recalls.Length, measured.Recalls.ToImmutableArray(),
            Percentile(sorted, VectorComparisonRunnerValues.P95Fraction), Percentile(sorted, VectorComparisonRunnerValues.P99Fraction), index.BuildMilliseconds,
            profile.IndexKind.ToString(), index.Definition, plan, index.Parameters,
            null, null, measured.QuerySeconds, measured.QuerySuccesses / measured.QuerySeconds,
            measured.UpdateSeconds, measured.UpdateSeconds == VectorComparisonRunnerValues.FirstIndex ? VectorComparisonRunnerValues.FirstIndex : measured.UpdateSuccesses / measured.UpdateSeconds);
        var result = new ComparisonCase(target.Name, Scenario.VectorExact, VectorComparisonRunnerValues.FirstIndex, ComparisonStatuses.Measured,
            null, null, []) { VectorMetrics = metrics };
        var report = new ComparisonReport(VectorComparisonRunnerValues.ReportSchemaVersion, Guid.NewGuid(), started, null, datasetHash,
            VectorComparisonRunnerValues.ClosedLoopVectorQueryThroughputIncludes,
            RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount,
            RuntimeInformation.FrameworkDescription, storage, sourceRevision, [target.Profile], [result]) { VectorProfile = profile };
        report.ValidateConfiguration();
        return report;
    }

    private static double Percentile(double[] sorted, double percentile)
        => sorted[Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - VectorComparisonRunnerValues.SingleElementOffset, VectorComparisonRunnerValues.FirstIndex, sorted.Length - VectorComparisonRunnerValues.SingleElementOffset)];

    private static string ComputeDatasetHash(VectorComparisonCorpus corpus, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(corpus.Profile.Id));
        foreach (var document in corpus.StreamDocuments())
        {
            if ((document.Number & VectorComparisonRunnerValues.CancellationChunkMask) == VectorComparisonRunnerValues.FirstIndex)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            hash.AppendData(Encoding.UTF8.GetBytes(document.Id));
            hash.AppendData(Convert.FromHexString(VectorComparisonCorpus.HashVector(document.Embedding.Span)));
            hash.AppendData(Convert.FromHexString(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(document.Payload)))));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static async IAsyncEnumerable<VectorDocument> StreamDocumentsAsync(VectorComparisonCorpus corpus,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var document in corpus.StreamDocuments())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return document;
            if ((document.Number & VectorComparisonRunnerValues.YieldBatchMask) == VectorComparisonRunnerValues.YieldBatchMask)
            {
                await Task.Yield();
            }
        }
    }
}
