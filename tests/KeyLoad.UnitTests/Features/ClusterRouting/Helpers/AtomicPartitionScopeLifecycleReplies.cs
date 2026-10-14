namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class AtomicPartitionScopeLifecycleReplies
{
    internal static async Task SuccessAsync(OperationResult result, CommandRequest request,
        AtomicPartitionScopeLifecycleState state, bool healthy)
    {
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.SafeDetail).IsNull();
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(request.CommandId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(state.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(request.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(1);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        var item = healthy ? "healthy" : "item";
        MutationReceipt[] expected = [new("putDocument", AtomicPartitionScopeLifecycleCommands.Documents, item, 1),
            new("appendEvents", AtomicPartitionScopeLifecycleCommands.Events, item, 1),
            new("enqueue", AtomicPartitionScopeLifecycleCommands.Queue, item, 1)];
        await AtomicPartitionScopeLifecycleModels.EqualAsync(receipt.Mutations.ToArray(), expected);
        foreach (var mutation in receipt.Mutations)
        {
            await Assert.That(mutation.CompositionReferences).IsEmpty();
        }
    }
    internal static async Task FailureAsync(OperationResult result, string detail)
    {
        await Assert.That(result.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(result.SafeDetail).IsEqualTo(detail);
        await Assert.That(result.Json).IsNull();
        await Assert.That(result.NativeValue).IsNull();
    }
}
