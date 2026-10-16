using KeyLoad.Core;
using KeyLoad.UnitTests.Features.ClusterRouting;
using K = KeyLoad.UnitTests.Features.Search.TextMaintenanceLifetimeNativeProtocol;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

namespace KeyLoad.UnitTests.Features.Search;

internal static class TextMaintenanceLifetimeNativeSeed
{
    internal static async Task<(TextIndexMaintenanceRequest Request, ReplicatedOperation Write, CommitReceipt Receipt)> CreateAsync(
        NativeLocalChildLifetimeSeeded state, CancellationToken token)
    {
        var database = state.Leader.Journal.Database;
        var collection = NativeTextBilingualAudit.Collection;
        _ = await SubmitAsync(state, OperationKind.ConfigureResource, Guid.NewGuid(),
            new ConfigureResourceRequest(P.Partition.TenantId, P.Partition.DatabaseId,
                new(collection, ResourceKind.Collection, P.Partition.TransactionDomainId)), token);
        var consumer = new ProjectionConsumerRef(P.Partition, K.Consumer);
        var configureId = Guid.NewGuid();
        _ = await SubmitAsync(state, OperationKind.ConfigureProjectionConsumer, configureId,
            new ConfigureProjectionConsumerRequest(configureId, consumer, new(K.Generation, [collection], K.Mutations)), token);
        var command = new CommandRequest(Guid.NewGuid(), P.Partition,
            [new PutDocument(collection, NativeTextBilingualAudit.UkrainianId, NativeTextBilingualAudit.UkrainianJson),
                new PutDocument(collection, NativeTextBilingualAudit.EnglishId, NativeTextBilingualAudit.EnglishJson)]);
        var index = checked(state.Leader.Journal.Log.State.LastIndex + K.NextIndex);
        var operation = database.CreateNativeOperation(OperationKind.Batch, command.CommandId, P.Root,
            database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(command));
        var receipt = (await state.Leader.Journal.Consensus.SubmitAsync(operation, token)).Get<CommitReceipt>();
        var expected = new CommitReceipt(command.CommandId,
            new(state.PhysicalOwner.Incarnation, P.Partition.AtomicPartitionId, index, state.PhysicalOwner.PlacementEpoch),
            [new(K.MutationKind, collection, NativeTextBilingualAudit.UkrainianId, K.Revision),
                new(K.MutationKind, collection, NativeTextBilingualAudit.EnglishId, K.Revision)], DurabilityProfile.ProcessDurable);
        await NativeLocalChildLifetimeAssertions.EqualAsync(expected, receipt);
        return (new(Guid.NewGuid(), consumer, collection, K.Field, K.Generation, database.Store.Identity.NodeId,
            state.PhysicalOwner, TextIndexMaintenanceMode.Build), operation, receipt);
    }

    internal static async Task<OperationResult> SubmitAsync<T>(NativeLocalChildLifetimeSeeded state,
        OperationKind kind, Guid id, T body, CancellationToken token)
    {
        var database = state.Leader.Journal.Database;
        var operation = database.CreateNativeOperation(kind, id, P.Root, database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(body));
        return await state.Leader.Journal.Consensus.SubmitAsync(operation, token);
    }
}
