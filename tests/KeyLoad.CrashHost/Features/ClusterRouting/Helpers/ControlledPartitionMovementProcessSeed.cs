namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Publishes the actual child mixed corpus through native log commit and original materialization.</summary>
internal static class ControlledPartitionMovementProcessSeed
{
    private const string CollectionIdText = "bde5e9ef-a97c-46b2-baf8-1c09f4ce0c30";
    private const string TopicIdText = "bf5a0b3b-23cb-40b6-90f0-44de34ae8d37";
    private const string QueueIdText = "c5db5247-e280-4197-bb22-c428ece23790";
    private const string PrincipalId = "root";
    private const string ConfigureFailure = "The original movement process resource configuration failed.";
    private static readonly Guid CollectionId = Guid.Parse(CollectionIdText);
    private static readonly Guid TopicId = Guid.Parse(TopicIdText);
    private static readonly Guid QueueId = Guid.Parse(QueueIdText);

    /// <summary>Submits original resource commands then the complete mixed native batch.</summary>
    /// <param name="node">The borrowed actual configured child owner.</param>
    /// <param name="cancellationToken">The original process operation cancellation.</param>
    /// <returns>The actual mixed outcome and original evaluation time for independent literal verification.</returns>
    internal static (OperationResult Outcome, DateTimeOffset EvaluatedAt) Submit(
        ControlledPartitionMovementNativeNode node, CancellationToken cancellationToken)
    {
        Configure(node, CollectionId, ControlledPartitionMovementProcessCorpus.Collection,
            ResourceKind.Collection, cancellationToken);
        Configure(node, TopicId, ControlledPartitionMovementProcessCorpus.Topic, ResourceKind.Topic, cancellationToken);
        Configure(node, QueueId, ControlledPartitionMovementProcessCorpus.Queue, ResourceKind.WorkQueue, cancellationToken);
        var evaluatedAt = node.Database.EvaluationClock.GetUtcNow();
        return (node.Journal.Submit(ControlledPartitionMovementProcessCorpus.SeedOperation(node.Database,
            evaluatedAt), cancellationToken), evaluatedAt);
    }

    private static void Configure(ControlledPartitionMovementNativeNode node, Guid commandId,
        string resource, ResourceKind kind, CancellationToken cancellationToken)
    {
        var partition = ControlledPartitionMovementProcessCorpus.Partition;
        var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId,
            new(resource, kind, partition.TransactionDomainId));
        var operation = node.Database.CreateNativeOperation(OperationKind.ConfigureResource, commandId,
            PrincipalId, node.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        if (node.Journal.Submit(operation, cancellationToken).Error is not null)
        { throw new InvalidOperationException(ConfigureFailure); }
    }
}
