using KeyLoad.Clustering;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaProtocolFileFailureFlow
{
    private const string Collection = "transport-lifetime";
    private const string Document = "original";
    private const string Json = "{\"state\":\"healthy\"}";
    private const string Missing = "original-protocol-cancellation-missing.bin";
    private const string Root = "root";
    private const int CallbackFailures = 1;

    internal static async Task RunAsync(RequestCqrsCohortRuntimeFixture runtime, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var db = new TestDatabase();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                db.Configure(Collection, ResourceKind.Collection);
                var command = new CommandRequest(Guid.NewGuid(), db.Partition, [new PutDocument(Collection, Document, Json, 0)]);
                var operation = new ReplicatedOperation(command.CommandId, OperationKind.Batch, Root,
                    db.Database.EvaluationClock.GetUtcNow(), System.Text.Encoding.UTF8.GetString(JsonDefaults.Serialize(command)));
                await FailedAsync(runtime, db, operation, token);
                await ReplicaTransportHealthyContinuation.RunAsync(runtime, db, operation, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task FailedAsync(RequestCqrsCohortRuntimeFixture runtime, TestDatabase db,
        ReplicatedOperation operation, CancellationToken token)
    {
        AggregateException? expected = null;
        var failures = new List<Exception>();
        var gate = new ReplicaProtocolFileFailureGate(Path.Combine(db.Directory, Missing));
        Task<ReadOnlyMemory<byte>>? submitted = null;
        Task? disposal = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            try
            {
                await using var endpoint = new ReplicaLifecycleFixture(configuration: runtime.Node(RequestCqrsCohortScenario.LocalVoter).Journal.Configuration);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    endpoint.Consensus.AttachTransport(runtime.LocalServices.GetRequiredService<ReplicaGrainServiceClient>());
                    endpoint.Consensus.ConfigureForwarding((input, cancellation) => gate.ForwardAsync(db.Database, input, cancellation));
                    var image = QueueWholeFlowStorage.Bytes(db.Store);
                    var position = db.Store.Position;
                    submitted = endpoint.Consensus.HandleAsync(ReplicaRpc.Forward, ReplicaForwardTestFrame.Payload(operation), token);
                    await gate.Entered.Task.WaitAsync(token);
                    disposal = endpoint.Consensus.DisposeAsync().AsTask();
                    await gate.Cancelled.Task.WaitAsync(token);
                    await Assert.That(disposal.IsCompleted).IsFalse();
                    await Assert.That(submitted.IsCompleted).IsFalse();
                    await Assert.That(gate.Exited.Task.IsCompleted).IsFalse();
                    await ReplicaTransportShutdownFlow.UnchangedAsync(db, image, position);
                    gate.Release();
                    expected = await Assert.ThrowsExactlyAsync<AggregateException>(() => disposal.WaitAsync(token));
                    await Assert.That(expected!.Flatten().InnerExceptions.Count).IsEqualTo(CallbackFailures);
                    await Assert.That(expected.Flatten().InnerExceptions.Single()).IsSameReferenceAs(gate.Failure);
                    var stopped = await Assert.ThrowsExactlyAsync<AggregateException>(() => endpoint.Consensus.StopAsync(token));
                    await Assert.That(stopped).IsSameReferenceAs(expected);
                    await Assert.That(endpoint.Consensus.DisposeAsync().AsTask()).IsSameReferenceAs(disposal);
                    await Assert.ThrowsAsync<OperationCanceledException>(() => submitted.WaitAsync(token));
                    await Assert.That(gate.Exited.Task.IsCompleted).IsTrue();
                    await ReplicaTransportShutdownFlow.UnchangedAsync(db, image, position);
                }, failures);
                gate.Release();
                if (submitted is not null)
                {
                    await ObserveOriginalAsync(submitted, expected is not null, failures);
                }
            }
            catch (Exception original) when (ReferenceEquals(original, expected))
            {
            }
        }, failures);
        gate.Release();
        if (submitted is not null)
        {
            await ObserveOriginalAsync(submitted, expected is not null, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static Task ObserveOriginalAsync(Task original, bool cancellationObserved, List<Exception> failures)
        => ServerFailureObserver.ObserveAsync(async () =>
        {
            try
            {
                await original.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationObserved)
            {
            }
        }, failures);
}
