using static KeyLoad.IntegrationTests.Features.ClusterReplication.ClusterReplicationTestSupport;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class LeaderLossQueueScenario
{
    private const string DocumentKey = "o1";
    private const string MessageKey = "m1";
    private const string Worker = "worker";
    private const string StatusPath = "/status";
    private const string CompletedJson = "\"done\"";
    private const long InitialRevision = 1;
    private const long ExecutionGeneration = 1;

    // AC-REP-003: every uncertain queue write retries the same frozen request and retains the effect oracle.
    internal static async Task ProcessAsync(LeaderLossRunState state, string collection, string queue,
        CancellationToken cancellationToken)
    {
        var surviving = state.Clients[state.Survivors[0]];
        var retried = await RetryDuringElectionAsync(() => surviving.CommitAsync(state.Command, cancellationToken), cancellationToken);
        await Assert.That(Success(retried).Token).IsEqualTo(state.Receipt.Token);
        await Assert.That(Success(await surviving.GetAsync(new(state.Partition, collection, DocumentKey), cancellationToken))!.Revision)
            .IsEqualTo(InitialRevision);
        var receive = new ReceiveRequest(Guid.NewGuid(), new(state.Partition, queue));
        var received = await RetryDuringElectionAsync(() => surviving.ReceiveAsync(receive, cancellationToken), cancellationToken);
        var delivery = await Assert.That(Success(received).Deliveries).HasSingleItem();
        var processing = new ProcessingRequest(Guid.NewGuid(), new(state.Partition, queue), delivery.Token, Worker, ExecutionGeneration,
            [new PatchDocument(collection, DocumentKey, [new(StatusPath, PatchKind.Set, CompletedJson)], InitialRevision)]);
        Success(await RetryDuringElectionAsync(() => surviving.CommitProcessingAsync(processing, cancellationToken), cancellationToken));
        await Assert.That(Success(await state.Clients[state.Survivors[1]].InspectAsync(new(new(state.Partition, queue), MessageKey), cancellationToken))!
            .Metadata.State).IsEqualTo(MessageState.Acked);
    }
}
