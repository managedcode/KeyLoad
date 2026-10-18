using KeyLoad.Core;
using KeyLoad.RecoveryTests.Features.EventStreams;
using static KeyLoad.RecoveryTests.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class SharedDomainProducerFlow
{
    internal static async Task RunAsync(DatabaseEngine database, SharedDomainState state, bool healthy, CancellationToken token)
    {
        var id = healthy ? Guid.NewGuid() : CommandId;
        foreach (var scope in state.Scopes)
        {
            var other = state.Scopes.Where(candidate => candidate != scope).ToDictionary(candidate => candidate.Partition,
                candidate => SharedDomainNativeImage.Capture(database.Store, candidate.Partition));
            var command = SharedDomainCorpus.Producer(scope, id, healthy ? Healthy : Item);
            var position = database.Store.Position + FirstRevision;
            var operation = SharedDomainOperations.Operation(OperationKind.Batch, command.CommandId, command);
            var result = SharedDomainOperations.Apply(database, operation, token);
            await SharedDomainValueAssertions.ReceiptAsync(result, command, database.Store.Identity, position);
            state.Recorded.Add(scope.Partition.AtomicPartitionId + IdentitySeparator + (healthy ? Healthy : Item),
                SharedDomainNativeImage.Clock(database.Store));
            await SharedDomainHistoryOperations.SaveAsync(database.Store, state, scope.Partition, operation, result);
            if (healthy)
            {
                state.Healthy.Add(scope.Partition.AtomicPartitionId);
            }
            else
            {
                state.Producers.Add(scope.Partition.AtomicPartitionId, result.Get<CommitReceipt>());
            }
            foreach (var pair in other)
            {
                await SharedDomainNativeImage.ScopeAsync(database.Store, pair.Key, pair.Value);
            }
        }
        await SharedDomainModelAssertions.AllAsync(database, state, token);
        await Assert.That(state.Producers.Values.Select(receipt => receipt.Token.AtomicPartitionId).Distinct().Count())
            .IsEqualTo(state.Scopes.Length);
    }
}
