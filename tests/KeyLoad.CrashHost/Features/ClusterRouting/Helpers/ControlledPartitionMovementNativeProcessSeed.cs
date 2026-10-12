using System.Collections.Immutable;
using KeyLoad.Orleans;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeProcessSeed
{
    private const string PrincipalId = "root";
    private const string CollectionCommand = "bde5e9ef-a97c-46b2-baf8-1c09f4ce0c30";
    private const string TopicCommand = "bf5a0b3b-23cb-40b6-90f0-44de34ae8d37";
    private const string QueueCommand = "c5db5247-e280-4197-bb22-c428ece23790";

    internal static async Task<ImmutableArray<GrainOperationReply>> SubmitAsync(
        ControlledPartitionMovementNativeHost source, CancellationToken cancellationToken)
    {
        var outcomes = ImmutableArray.CreateBuilder<GrainOperationReply>();
        foreach (var original in new[]
        {
            (CollectionCommand, ControlledPartitionMovementProcessCorpus.Collection, ResourceKind.Collection),
            (TopicCommand, ControlledPartitionMovementProcessCorpus.Topic, ResourceKind.Topic),
            (QueueCommand, ControlledPartitionMovementProcessCorpus.Queue, ResourceKind.WorkQueue)
        })
        {
            var partition = ControlledPartitionMovementProcessCorpus.Partition;
            var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId,
                new(original.Item2, original.Item3, partition.TransactionDomainId));
            var reply = await source.SubmitAsync(OperationKind.ConfigureResource, Guid.Parse(original.Item1),
                PrincipalId, NativeSerialization.Serialize(request), cancellationToken).ConfigureAwait(false);
            outcomes.Add(reply);
            if (reply.Error is not null)
            { return outcomes.ToImmutable(); }
        }
        outcomes.Add(await source.SubmitAsync(OperationKind.Batch,
            ControlledPartitionMovementProcessCorpus.SeedId, PrincipalId,
            NativeSerialization.Serialize(ControlledPartitionMovementProcessCorpus.Seed()),
            cancellationToken).ConfigureAwait(false));
        return outcomes.ToImmutable();
    }
}
