using StackExchange.Redis;

namespace KeyLoad.Comparisons.Targets;

public sealed partial class RedisTarget
{
    private const string FinalPrimaryFailure = "RedisFinalDocumentPrimaryUnavailable", FinalFenceSuffix = "-final-proof";
    private const char NamespaceSeparator = ':';
    private RedisDocumentNamespace? documentNamespace;
    /// <inheritdoc/>
    public Task InitializeDocumentsAsync(DocumentComparisonInitialization initialization, CancellationToken token)
    {
        DocumentNativeInitialization.Validate(initialization);
        documentNamespace = new(prefix, initialization.MaximumIdentityExclusive, executionOptions.Value.ReadbackBatchCapacity);
        return InitializeAsync(initialization.Corpus, token);
    }
    /// <inheritdoc/>
    public Task<IDocumentComparisonSession> OpenDocumentSessionAsync(CancellationToken token)
        => DocumentNativeSessionAcquisition<ConnectionMultiplexer>.OpenAsync(
            _ => ConnectionMultiplexer.ConnectAsync(RedisReplicaProof.CreateOptions(connectionSettings)), AdmitDocumentConnectionAsync,
            actual => new RedisComparisonSession(actual, prefix, configuredTopology,
                -DocumentMeasurementValues.SingleItemCount, lifecycleOptions, executionOptions, joinOriginalOperations: true),
            token, (actual, cancellation) => documentNamespace!.ReadAsync(actual, cancellation));
    private async Task AdmitDocumentConnectionAsync(ConnectionMultiplexer actual, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await RedisReplicaProof.VerifyWorkerPrimaryAsync(actual, configuredTopology, token).ConfigureAwait(false);
    }
    /// <inheritdoc/>
    public async Task VerifyDocumentCopiesAsync(DocumentComparisonSchedule schedule, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (connection is null)
        {
            throw new ComparisonFailureException(FinalPrimaryFailure);
        }

        var evidence = await RedisDocumentCopyProof.VerifyAsync(connection, replicaEndpoints, configuredTopology, Profile,
            prefix.TrimEnd(NamespaceSeparator) + FinalFenceSuffix, schedule, lifecycleOptions, timeProvider,
            (replica, token) => documentNamespace!.ReadAsync(replica, token, CommandFlags.DemandReplica), cancellationToken).ConfigureAwait(false);
        Profile = Profile with { Cluster = evidence };
    }
    private Task CleanupDocumentNamespaceAsync()
        => documentNamespace is null || connection is null ? Task.CompletedTask
            : documentNamespace.CleanupAsync(connection, executionOptions.Value.DocumentCleanupTimeout, timeProvider);

}
