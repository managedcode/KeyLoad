using KeyLoad.Clustering;
using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Retains all original mixed-model seed commands through actual fresh signed native execution.</summary>
internal static class ControlledPartitionMovementNativeSeed
{
    private static readonly Guid CollectionId = Guid.Parse("bde5e9ef-a97c-46b2-baf8-1c09f4ce0c30");
    private static readonly Guid TopicId = Guid.Parse("bf5a0b3b-23cb-40b6-90f0-44de34ae8d37");
    private static readonly Guid QueueId = Guid.Parse("c5db5247-e280-4197-bb22-c428ece23790");

    internal static async Task<GrainOperationReply> ExecuteAsync(ControlledPartitionMovementNativeHost source,
        CancellationToken cancellationToken)
    {
        await ConfigureAsync(source, CollectionId, ControlledPartitionMovementCorpus.Collection,
            ResourceKind.Collection, cancellationToken);
        await ConfigureAsync(source, TopicId, ControlledPartitionMovementCorpus.Topic,
            ResourceKind.Topic, cancellationToken);
        await ConfigureAsync(source, QueueId, ControlledPartitionMovementCorpus.Queue,
            ResourceKind.WorkQueue, cancellationToken);
        var original = await source.SubmitAsync(OperationKind.Batch, ControlledPartitionMovementCorpus.SeedId,
            PhysicalShardCatalogFixture.RootPrincipalId,
            NativeSerialization.Serialize(ControlledPartitionMovementCorpus.Seed()), cancellationToken);
        await Assert.That(original.Error).IsNull();
        await Assert.That(original.SafeDetail).IsNull();
        await Assert.That(original.Payload.IsEmpty).IsFalse();
        return original;
    }

    private static async Task ConfigureAsync(ControlledPartitionMovementNativeHost source, Guid commandId,
        string resource, ResourceKind kind, CancellationToken cancellationToken)
    {
        var partition = ControlledPartitionMovementCorpus.Partition;
        var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId,
            new(resource, kind, partition.TransactionDomainId));
        var reply = await source.SubmitAsync(OperationKind.ConfigureResource, commandId,
            PhysicalShardCatalogFixture.RootPrincipalId, NativeSerialization.Serialize(request), cancellationToken);
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
    }

    internal static async Task<CommitReceipt> ReceiptAsync(ControlledPartitionMovementNativeHost source,
        GrainOperationReply reply, ReplicaEntry original)
    {
        var expected = new CommitReceipt(ControlledPartitionMovementCorpus.SeedId,
            new(source.Store.Identity.Incarnation, ControlledPartitionMovementCorpus.Partition.AtomicPartitionId,
                original.Index, ControlledPartitionMovementCorpus.InitialRevision),
            ControlledPartitionMovementCorpus.ExpectedMutations(), DurabilityProfile.QuorumProcessDurable);
        await ReceiptAsync(reply, expected);
        return expected;
    }

    internal static async Task ReceiptAsync(GrainOperationReply reply, CommitReceipt expected)
    {
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
        var receipt = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as CommitReceipt;
        await Assert.That(JsonDefaults.Serialize(receipt).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

}
