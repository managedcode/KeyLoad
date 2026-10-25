using static KeyLoad.ClusterTests.StorageReplication.ClusterReplicationTestSupport;

namespace KeyLoad.ClusterTests.StorageReplication;

internal static class SiloRestartQueueScenario
{
    private const string DocumentKey = "o1";
    private const string MessageKey = "m1";
    private const string Worker = "worker";
    private const string StatusPath = "/status";
    private const string CompletedJson = "\"done\"";
    private const long InitialRevision = 1;
    private const long ExecutionGeneration = 1;

    // AC-REP-003: every uncertain queue write retries the same frozen request and retains the effect oracle.
    internal static async Task ProcessAsync(SiloRestartRunState state, string collection, string queue,
        CancellationToken cancellationToken)
    {
        var surviving = state.Clients[state.Survivors[0]];
        var retried = await RetryDuringSiloRecoveryAsync(() => surviving.CommitAsync(state.Command, cancellationToken), cancellationToken);
        await Assert.That(Success(retried).Token).IsEqualTo(state.Receipt.Token);
        await Assert.That(Success(await surviving.GetAsync(new(state.Partition, collection, DocumentKey), cancellationToken)).Document!.Revision)
            .IsEqualTo(InitialRevision);
        var receive = new ReceiveRequest(Guid.NewGuid(), new(state.Partition, queue));
        var received = await RetryDuringSiloRecoveryAsync(() => surviving.ReceiveAsync(receive, cancellationToken), cancellationToken);
        var delivery = await Assert.That(Success(received).Deliveries).HasSingleItem();
        var processing = new ProcessingRequest(Guid.NewGuid(), new(state.Partition, queue), delivery.Token, Worker, ExecutionGeneration,
            [new PatchDocument(collection, DocumentKey, [new(StatusPath, PatchKind.Set, CompletedJson)], InitialRevision)]);
        Success(await RetryDuringSiloRecoveryAsync(() => surviving.CommitProcessingAsync(processing, cancellationToken), cancellationToken));
        await Assert.That(Success(await state.Clients[state.Survivors[1]].InspectAsync(new(new(state.Partition, queue), MessageKey), cancellationToken)).Message!
            .Metadata.State).IsEqualTo(MessageState.Acked);
    }
}
