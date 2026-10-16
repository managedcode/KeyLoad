using KeyLoad.UnitTests.Features.ClusterRouting;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementQuiescenceData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementQuiescenceBusiness
{
    internal static async Task WriteAsync(ResourceRetirementQuiescenceState state, int pass, CancellationToken token)
    {
        if (pass == Zero)
        {
            var definition = new ResourceDefinition(NewResource, ResourceKind.Collection, Partition.TransactionDomainId);
            var configured = Issue(state.Source, OperationKind.ConfigureResource, Guid.NewGuid(),
                new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId, definition));
            var result = state.Source.Journal.Submit(configured, token);
            await Assert.That(result.Error).IsNull();
            await ResourceRetirementQuiescenceAssertions.EqualAsync(definition, result.Get<ResourceDefinition>());
            state.Originals.Add((state.Source, configured, result));
        }
        foreach (var collection in new[] { ControlledPartitionMovementCorpus.Collection, NewResource })
        {
            var id = NewDocument + pass.ToString(System.Globalization.CultureInfo.InvariantCulture) + collection;
            var commandId = Guid.NewGuid();
            var original = Issue(state.Source, OperationKind.Batch, commandId,
                new CommandRequest(commandId, OtherPartition, [new PutDocument(collection, id, ResourceRetirementQuiescenceData.Json)]));
            var index = state.Source.Journal.Log.State.LastIndex + One;
            var result = state.Source.Journal.Submit(original, token);
            await Assert.That(result.Error).IsNull();
            await ResourceRetirementQuiescenceAssertions.EqualAsync(new CommitReceipt(commandId,
                new(state.Source.PhysicalOwner.Incarnation, OtherPartition.AtomicPartitionId, index, One),
                [new(PutDocumentKind, collection, id, One)], state.Source.Database.Durability), result.Get<CommitReceipt>());
            await ResourceRetirementQuiescenceAssertions.EqualAsync(new DocumentResult(new(OtherPartition, collection, id),
                One, ResourceRetirementQuiescenceData.Json, false, []), state.Source.Database.GetDocument(Root, new(OtherPartition, collection, id), cancellationToken: token)!);
            state.Originals.Add((state.Source, original, result));
        }
    }

    private const string PutDocumentKind = "putDocument";

    private static ReplicatedOperation Issue<T>(ControlledPartitionMovementNode owner, OperationKind kind, Guid id, T request)
        => owner.Database.CreateNativeOperation(kind, id, Root, owner.Database.EvaluationClock.GetUtcNow(),
            NativeSerialization.Serialize(request));
}
