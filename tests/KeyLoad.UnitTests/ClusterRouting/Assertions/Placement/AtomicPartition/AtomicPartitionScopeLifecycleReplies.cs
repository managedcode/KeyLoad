using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class AtomicPartitionScopeLifecycleReplies
{
    internal static async Task SuccessAsync(Result<object> result, CommandRequest request,
        AtomicPartitionScopeLifecycleState state, bool healthy)
    {
        await Assert.That(Errors.Code(result.Problem)).IsNull();
        await Assert.That(result.Problem?.Detail).IsNull();
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(request.CommandId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(state.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(request.Partition.AtomicPartitionId);

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
    internal static async Task FailureAsync(Result<object> result, string detail)
    {
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(result.Problem?.Detail).IsEqualTo(detail);

        await Assert.That(result.Value).IsNull();
    }
}
