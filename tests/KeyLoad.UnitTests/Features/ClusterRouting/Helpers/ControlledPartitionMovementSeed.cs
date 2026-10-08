using KeyLoad.Core;
using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Seeds original canonical mixed-model commands through the same actual replica owner.</summary>
internal static class ControlledPartitionMovementSeed
{
    private const string ConfigureFailure = "The native movement corpus resource configuration failed.";
    private static readonly Guid CollectionCommandId = Guid.Parse("bde5e9ef-a97c-46b2-baf8-1c09f4ce0c30");
    private static readonly Guid TopicCommandId = Guid.Parse("bf5a0b3b-23cb-40b6-90f0-44de34ae8d37");
    private static readonly Guid QueueCommandId = Guid.Parse("c5db5247-e280-4197-bb22-c428ece23790");

    internal static OperationResult Submit(DatabaseEngine database,
        ControlledPartitionMovementNativeJournal journal, out DateTimeOffset evaluatedAt, CancellationToken cancellationToken)
    {
        Configure(database, journal, CollectionCommandId,
            ControlledPartitionMovementCorpus.Collection, ResourceKind.Collection, cancellationToken);
        Configure(database, journal, TopicCommandId,
            ControlledPartitionMovementCorpus.Topic, ResourceKind.Topic, cancellationToken);
        Configure(database, journal, QueueCommandId,
            ControlledPartitionMovementCorpus.Queue, ResourceKind.WorkQueue, cancellationToken);
        evaluatedAt = database.EvaluationClock.GetUtcNow();
        return journal.Submit(ControlledPartitionMovementCorpus.SeedOperation(database, evaluatedAt), cancellationToken);
    }

    private static void Configure(DatabaseEngine database, ControlledPartitionMovementNativeJournal journal,
        Guid commandId, string resource, ResourceKind kind, CancellationToken cancellationToken)
    {
        var partition = ControlledPartitionMovementCorpus.Partition;
        var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId,
            new(resource, kind, partition.TransactionDomainId));
        var operation = database.CreateNativeOperation(OperationKind.ConfigureResource, commandId,
            PhysicalShardCatalogFixture.RootPrincipalId, database.EvaluationClock.GetUtcNow(),
            NativeSerialization.Serialize(request));
        var result = journal.Submit(operation, cancellationToken);
        if (result.Error is not null)
        {
            throw new InvalidOperationException(ConfigureFailure);
        }
    }
}
