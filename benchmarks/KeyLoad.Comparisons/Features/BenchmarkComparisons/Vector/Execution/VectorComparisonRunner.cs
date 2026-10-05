using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.CompilerServices;

namespace KeyLoad.Comparisons;

/// <summary>Runs one profile-bound vector workload against a real native vector target.</summary>
public sealed class VectorComparisonRunner(VectorComparisonProfile profile)
{
    private readonly VectorComparisonProfile profile = profile ?? throw new ArgumentNullException(nameof(profile));

    public async Task<ComparisonReport> RunAsync(IVectorComparisonTarget target, string? sourceRevision,
        string storage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!target.Supports(profile.IndexKind, profile.QueryMode))
            throw new NotSupportedException($"{target.Name} does not implement {profile.IndexKind}/{profile.QueryMode} natively.");

        var started = TimeProvider.System.GetUtcNow();
        var corpus = new VectorComparisonCorpus(profile);
        var datasetHash = ComputeDatasetHash(corpus, cancellationToken);
        var loaded = await target.IngestAsync(StreamDocumentsAsync(corpus, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (loaded != profile.RecordCount) throw new InvalidDataException($"Native ingestion loaded {loaded} records; expected {profile.RecordCount}.");
        await ValidateReadbackAsync(target, corpus, cancellationToken).ConfigureAwait(false);

        var index = await target.BuildIndexAsync(profile, cancellationToken).ConfigureAwait(false);
        if (index.IndexKind != profile.IndexKind || !double.IsFinite(index.BuildMilliseconds) || index.BuildMilliseconds < 0
            || (profile.IndexKind == VectorIndexKind.Exact && index.BuildMilliseconds != 0))
            throw new InvalidDataException("Native index receipt does not match the selected profile.");

        var queries = corpus.CreateQueries();
        var expected = new IReadOnlyList<VectorNeighbor>[queries.Count];
        for (var i = 0; i < queries.Count; i++) expected[i] = corpus.ExactNeighbors(queries[i], cancellationToken);
        var plan = await target.ExplainAsync(queries[0], profile.QueryMode, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(plan)) throw new InvalidDataException("Native query plan is empty.");
        if (profile.IndexKind is VectorIndexKind.Hnsw or VectorIndexKind.IvfFlat
            && !plan.Contains(profile.IndexKind == VectorIndexKind.Hnsw ? "hnsw" : "ivfflat", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The measured PostgreSQL query plan does not prove the requested ANN index is used.");
        if (profile.IndexKind == VectorIndexKind.NativeAnn && string.IsNullOrWhiteSpace(index.Definition))
            throw new InvalidDataException("NativeANN did not provide its actual algorithm configuration.");

        for (var i = 0; i < profile.WarmupQueries; i++)
            await ValidateQueryAsync(target, corpus, queries[i % queries.Count], expected[i % queries.Count], cancellationToken).ConfigureAwait(false);

        var latencies = new double[profile.LatencySampleCount];
        var recalls = new double[profile.MeasuredQueries];
        var successful = 0;
        var next = -1;
        using var gate = new Barrier(profile.Concurrency + 1 + (profile.UpdateCount > 0 ? 1 : 0));
        var queryClock = Stopwatch.StartNew();
        var queryActive = 1;
        var updateActive = profile.UpdateCount > 0 ? 1 : 0;
        var queriesDuringUpdates = 0;
        var updatesDuringQueries = 0;
        var queryWorkers = Enumerable.Range(0, profile.Concurrency).Select(_ => Task.Run(async () =>
        {
            gate.SignalAndWait(cancellationToken);
            while (true)
            {
                var operation = Interlocked.Increment(ref next);
                if (operation >= profile.MeasuredQueries) break;
                var queryIndex = operation % queries.Count;
                var sampleOrdinal = SampleOrdinal(operation, profile.MeasuredQueries, profile.LatencySampleCount);
                var timer = sampleOrdinal < 0 ? null : Stopwatch.StartNew();
                var neighbors = await target.SearchAsync(queries[queryIndex], profile.TopK, profile.QueryMode, cancellationToken).ConfigureAwait(false);
                if (timer is not null)
                {
                    timer.Stop();
                    latencies[sampleOrdinal] = timer.Elapsed.TotalMilliseconds;
                }
                recalls[operation] = ValidateNeighbors(corpus, neighbors, expected[queryIndex]);
                if (Volatile.Read(ref updateActive) != 0) Interlocked.Increment(ref queriesDuringUpdates);
                Interlocked.Increment(ref successful);
            }
            Volatile.Write(ref queryActive, 0);
        }, cancellationToken)).ToArray();

        var updateAttempts = 0;
        var updateSuccesses = 0;
        var updateElapsed = 0d;
        Task updateWorker = Task.CompletedTask;
        if (profile.UpdateCount > 0)
        {
            updateWorker = Task.Run(async () =>
            {
                gate.SignalAndWait(cancellationToken);
                var timer = Stopwatch.StartNew();
                for (var ordinal = 0; ordinal < profile.UpdateCount; ordinal++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var update = corpus.CreateUpdate(ordinal);
                    Interlocked.Increment(ref updateAttempts);
                    await target.UpdateAsync(update, cancellationToken).ConfigureAwait(false);
                    var actual = await target.ReadAsync(update.Id, cancellationToken).ConfigureAwait(false);
                    if (actual is null || actual.Number != update.Number || actual.Dimensions != profile.Dimensions
                        || actual.VectorSha256 != VectorComparisonCorpus.HashVector(update.Embedding.Span))
                        throw new InvalidDataException("An acknowledged embedding update failed native readback verification.");
                    Interlocked.Increment(ref updateSuccesses);
                    if (Volatile.Read(ref queryActive) != 0) Interlocked.Increment(ref updatesDuringQueries);
                }
                timer.Stop();
                updateElapsed = timer.Elapsed.TotalSeconds;
                Volatile.Write(ref updateActive, 0);
            }, cancellationToken);
        }
        gate.SignalAndWait(cancellationToken);
        try { await Task.WhenAll(queryWorkers.Append(updateWorker)).ConfigureAwait(false); }
        finally { gate.Dispose(); }
        queryClock.Stop();

        if (successful != profile.MeasuredQueries || updateSuccesses != profile.UpdateCount)
            throw new InvalidDataException("The vector workload did not complete its exact operation schedule.");
        if (profile.UpdateCount > 0 && (queriesDuringUpdates == 0 || updatesDuringQueries == 0))
            throw new InvalidDataException("Mixed vector searches and updates did not overlap in both directions.");
        var averageRecall = recalls.Average();
        var minimumRecall = recalls.Min();
        if (minimumRecall < profile.MinimumRecall || (profile.IndexKind == VectorIndexKind.Exact && averageRecall != 1d))
            throw new InvalidDataException($"Vector recall {averageRecall:F6} (minimum {minimumRecall:F6}) is below the profile contract.");

        var sorted = latencies.Order().ToArray();
        var metrics = new VectorMetrics(profile.RecordCount, loaded, profile.MeasuredQueries, successful,
            updateAttempts, updateSuccesses, averageRecall, minimumRecall, recalls.Length, recalls.ToImmutableArray(),
            Percentile(sorted, 0.95), Percentile(sorted, 0.99), index.BuildMilliseconds,
            profile.IndexKind.ToString(), index.Definition, plan, index.Parameters,
            null, null, queryClock.Elapsed.TotalSeconds, successful / queryClock.Elapsed.TotalSeconds,
            updateElapsed, updateElapsed == 0 ? 0 : updateSuccesses / updateElapsed);
        var result = new ComparisonCase(target.Name, Scenario.VectorExact, 0, ComparisonStatuses.Measured,
            null, null, []) { VectorMetrics = metrics };
        var report = new ComparisonReport(3, Guid.NewGuid(), started, null, datasetHash,
            "closed-loop vector queries; setup, oracle, validation, index build and warmup excluded",
            RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount,
            RuntimeInformation.FrameworkDescription, storage, sourceRevision, [target.Profile], [result]) { VectorProfile = profile };
        report.ValidateConfiguration();
        return report;
    }

    private async Task ValidateReadbackAsync(IVectorComparisonTarget target, VectorComparisonCorpus corpus,
        CancellationToken cancellationToken)
    {
        var expectedNumber = 0;
        await foreach (var actual in target.ReadbackAsync(cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (actual.Number != expectedNumber || actual.Dimensions != profile.Dimensions)
                throw new InvalidDataException("Native vector readback has an unexpected order, count, or dimension.");
            var expected = corpus.Create(expectedNumber++);
            if (actual.Id != expected.Id || actual.VectorSha256 != VectorComparisonCorpus.HashVector(expected.Embedding.Span)
                || actual.PayloadSha256 != Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(expected.Payload))))
                throw new InvalidDataException("Native vector readback differs from canonical corpus content.");
        }
        if (expectedNumber != profile.RecordCount)
            throw new InvalidDataException($"Native readback returned {expectedNumber} records; expected {profile.RecordCount}.");
    }

    private async Task ValidateQueryAsync(IVectorComparisonTarget target, VectorComparisonCorpus corpus,
        ReadOnlyMemory<float> query, IReadOnlyList<VectorNeighbor> expected, CancellationToken cancellationToken)
        => ValidateNeighbors(corpus, await target.SearchAsync(query, profile.TopK, profile.QueryMode, cancellationToken).ConfigureAwait(false), expected);

    private double ValidateNeighbors(VectorComparisonCorpus corpus, IReadOnlyList<VectorNeighbor> actual,
        IReadOnlyList<VectorNeighbor> expected)
    {
        var expectedCount = Math.Min(profile.TopK, expected.Count);
        if (actual.Count != expectedCount) throw new InvalidDataException("A vector response was underfilled or overfilled.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var neighbor in actual)
        {
            if (!ids.Add(neighbor.Id) || !double.IsFinite(neighbor.Distance))
                throw new InvalidDataException("A vector response contains duplicate IDs or non-finite distances.");
            if (profile.QueryMode is VectorQueryMode.Filtered or VectorQueryMode.Mixed)
            {
                if (!int.TryParse(neighbor.Id.AsSpan(1), out var number) || !corpus.Eligible(number))
                    throw new InvalidDataException("A native filtered vector response included an ineligible document.");
            }
        }
        var expectedIds = expected.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var matching = 0;
        foreach (var item in actual)
            if (expectedIds.Contains(item.Id)) matching++;
        return (double)matching / expectedCount;
    }

    private static int SampleOrdinal(int operation, int operationCount, int sampleCount)
    {
        var ordinal = (int)((long)operation * sampleCount / operationCount);
        return ordinal < sampleCount && (int)((long)(operation + 1) * sampleCount / operationCount) != ordinal
            ? ordinal : -1;
    }

    private static double Percentile(double[] sorted, double percentile)
        => sorted[Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1)];

    private static string ComputeDatasetHash(VectorComparisonCorpus corpus, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(corpus.Profile.Id));
        foreach (var document in corpus.StreamDocuments())
        {
            if ((document.Number & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
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
            if ((document.Number & 255) == 255) await Task.Yield();
        }
    }
}
