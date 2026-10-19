using KeyLoad.Core;
using KeyLoad.RecoveryTests.Features.EventStreams;
using KeyLoad.UnitTests.Features.ResourceExecution;
using static KeyLoad.RecoveryTests.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class SharedDomainProcessingFlow
{
    internal static async Task RunAsync(DatabaseEngine database, SharedDomainState state, CancellationToken token)
    {
        var scope = state.Scopes[FirstIndex];
        var other = state.Scopes.Skip(SecondIndex).ToDictionary(candidate => candidate.Partition,
            candidate => SharedDomainImage.Capture(database.Store, candidate.Partition));
        var lane = new QueueLaneRef(scope.Partition, scope.Queue!);
        var request = new ReceiveRequest(Guid.NewGuid(), lane);
        var operation = SharedDomainOperations.Operation(database, OperationKind.Receive, request.RequestId, request);
        var started = TimeProvider.System.GetUtcNow();
        var expectedPosition = database.Store.Position + FirstRevision;
        var received = SharedDomainOperations.Apply(database, operation, token);
        var ended = TimeProvider.System.GetUtcNow();
        var delivery = await Assert.That(received.Get<ReceiveResult>().Deliveries).HasSingleItem();
        var claims = new DeliveryClaims(lane, Item, Writer, FirstRevision, Generation, database.Store.Identity.Incarnation);
        await SharedDomainValueAssertions.EqualAsync(database.Verify<DeliveryClaims>(delivery.Token), claims);
        await SharedDomainValueAssertions.EqualAsync(delivery, new Delivery(Item, scope.Payload, Headers,
            delivery.Token, FirstRevision, delivery.LeaseUntil, checked((int)FirstRevision), Generation));
        await Assert.That(delivery.LeaseUntil >= started.AddSeconds(request.LeaseSeconds)
            && delivery.LeaseUntil <= ended.AddSeconds(request.LeaseSeconds)).IsTrue();
        await Assert.That(received.Get<ReceiveResult>().RequestId).IsEqualTo(request.RequestId);
        await Assert.That(received.Get<ReceiveResult>().Token).IsEqualTo(new CommitToken(database.Store.Identity.Incarnation,
            scope.Partition.AtomicPartitionId, expectedPosition));
        var before = SharedDomainImage.Capture(database.Store);
        var position = database.Store.Position;
        await ReplayResultAssertions.Same<ReceiveResult>(SharedDomainOperations.Apply(database, operation, token), received);
        await SharedDomainImage.SameAsync(database.Store, before, position);
        await SharedDomainHistoryOperations.SaveAsync(database.Store, state, scope.Partition, operation, received);
        var processing = SharedDomainCorpus.Processing(scope, delivery.Token, Guid.NewGuid());
        await SharedDomainRefusals.ConsumerAsync(database, state, processing, token);
        var processingOperation = SharedDomainOperations.Operation(database, OperationKind.Processing, processing.CommandId, processing);
        var processingPosition = database.Store.Position + FirstRevision;
        var completed = SharedDomainOperations.Apply(database, processingOperation, token);
        await SharedDomainValueAssertions.ProcessingAsync(completed, processing, database.Store.Identity, processingPosition);
        state.Processed = true;
        state.Recorded.Add(scope.Partition.AtomicPartitionId + IdentitySeparator + Processed, SharedDomainImage.Clock(database.Store));
        await SharedDomainHistoryOperations.SaveAsync(database.Store, state, scope.Partition, processingOperation, completed);
        foreach (var pair in other)
        {
            await SharedDomainImage.ScopeAsync(database.Store, pair.Key, pair.Value);
        }
        await SharedDomainModelAssertions.AllAsync(database, state, token);
        await SharedDomainInboxAssertions.RequireAsync(database.Store, processing, completed.Get<CommitReceipt>());
    }
}
