using KeyLoad.Clustering;
using KeyLoad.Replication;
using KeyLoad.UnitTests.Features.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaTransportHealthyContinuation
{
    private const string Root = "root";
    private const string Collection = "transport-lifetime";
    private const string Id = "original";
    private const string Json = "{\"state\":\"healthy\"}";
    private const long InitialRevision = 1;
    private const long InitialTail = 1;
    private const long InitialOwnershipEpoch = 1;
    private const long NextCommit = 1;

    internal static async Task RunAsync(RequestCqrsCohortRuntimeFixture runtime, TestDatabase db,
        ReplicatedOperation operation, CancellationToken token)
    {
        var failures = new List<Exception>();
        Task<ReadOnlyMemory<byte>>? submitted = null;
        var gate = new ReplicaTransportFailureGate("No cancellation is requested for the healthy owner.");
        await RequestCqrsCohortCleanup.CaptureAsync(async () =>
        {
            await using var endpoint = new ReplicaLifecycleFixture(configuration: runtime.Node(RequestCqrsCohortScenario.LocalVoter).Journal.Configuration);
            var state = ReplicaTransportShutdownFlow.State(runtime);
            await RequestCqrsCohortCleanup.CaptureAsync(async () =>
            {
                await using var native = ReplicaTransportShutdownFlow.Native(runtime, state);
                await RequestCqrsCohortCleanup.CaptureAsync(() => BodyAsync(runtime, db, operation,
                    endpoint, state, native, gate, task => submitted = task, token), failures);
                gate.Release();
                if (submitted is { } original)
                {
                    await RequestCqrsCohortCleanup.CaptureAsync(() => original, failures);
                }
            }, failures);
        }, failures);
        RequestCqrsCohortCleanup.ThrowIfAny(failures);
    }

    private static async Task BodyAsync(RequestCqrsCohortRuntimeFixture runtime, TestDatabase db,
        ReplicatedOperation operation, ReplicaLifecycleFixture endpoint, ReplicaSiloDiscoveryState state,
        ReplicaNativeDiscoveryRuntime native, ReplicaTransportFailureGate gate,
        Action<Task<ReadOnlyMemory<byte>>> retain, CancellationToken token)
    {
        endpoint.Consensus.AttachTransport(runtime.LocalServices.GetRequiredService<ReplicaGrainServiceClient>());
        endpoint.Consensus.ConfigureForwarding((input, cancellation) => gate.ForwardAsync(db.Database, input, cancellation));
        state.MarkTransportReady();
        var request = ReplicaTransportShutdownFlow.Request(runtime, state);
        var reply = runtime.Node(RequestCqrsCohortScenario.FirstRemote).Authentication.VerifyNativeDiscoveryReply(
            request, await native.DiscoverAsync(request, token));
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.Payload.Span.SequenceEqual(NativeSerialization.Serialize(state.Read()))).IsTrue();
        var originalIdentity = db.Store.Identity;
        var expectedPosition = db.Store.Position + NextCommit;
        var submitted = endpoint.Consensus.HandleAsync(ReplicaRpc.Forward, ReplicaProtocolCodec.Serialize(operation), token);
        retain(submitted);
        await gate.Entered.Task.WaitAsync(token);
        gate.Release();
        var receipt = ReplicaProtocolCodec.Deserialize<OperationResult>((await submitted).Span).Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(operation.Id);
        var expectedReceipt = new CommitReceipt(operation.Id,
            new(originalIdentity.Incarnation, db.Partition.AtomicPartitionId, expectedPosition, InitialOwnershipEpoch),
            [new("putDocument", Collection, Id, InitialRevision)], originalIdentity.Durability);
        await Assert.That(JsonDefaults.Serialize(receipt).SequenceEqual(JsonDefaults.Serialize(expectedReceipt))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(receipt.Mutations).SequenceEqual(JsonDefaults.Serialize(
            new MutationReceipt[] { new("putDocument", Collection, Id, InitialRevision) }))).IsTrue();
        var document = db.Database.GetDocument(Root, new(db.Partition, Collection, Id), cancellationToken: token);
        await Assert.That(JsonDefaults.Serialize(document).SequenceEqual(JsonDefaults.Serialize(
            new DocumentResult(new(db.Partition, Collection, Id), InitialRevision, Json, false, [])))).IsTrue();
        await Assert.That(db.Database.GetOutboxStatus(Root, db.Partition).Head.Tail).IsEqualTo(InitialTail);
        var image = QueueWholeFlowStorage.Bytes(db.Store);
        var cut = db.Store.Position;
        var replay = ReplicaProtocolCodec.Deserialize<OperationResult>((await endpoint.Consensus.HandleAsync(
            ReplicaRpc.Forward, ReplicaProtocolCodec.Serialize(operation), token)).Span).Get<CommitReceipt>();
        await Assert.That(JsonDefaults.Serialize(replay).SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
        await ReplicaTransportShutdownFlow.UnchangedAsync(db, image, cut);
    }
}
