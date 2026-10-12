using KeyLoad.Orleans;
using KeyLoad.Replication;
using KeyLoad.UnitTests.Features.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaTransportShutdownFlow
{
    private const string Root = "root";
    private const string Collection = "transport-lifetime";
    private const string Id = "original";
    private const string Json = "{\"state\":\"healthy\"}";
    private const int DiscoveryFailureIndex = 0;
    private const int EndpointFailureIndex = 1;
    private const int ShutdownFailures = 2;
    private const int SingleCallbackFailure = 1;
    private const int OriginalCallbackIndex = 0;

    internal static async Task RunAsync(RequestCqrsCohortRuntimeFixture runtime, CancellationToken token)
    {
        var failures = new List<Exception>();
        await RequestCqrsCohortCleanup.CaptureAsync(async () =>
        {
            using var db = new TestDatabase();
            await RequestCqrsCohortCleanup.CaptureAsync(() => RunOwnedAsync(runtime, db, token), failures);
        }, failures);
        RequestCqrsCohortCleanup.ThrowIfAny(failures);
    }

    private static async Task RunOwnedAsync(RequestCqrsCohortRuntimeFixture runtime, TestDatabase db, CancellationToken token)
    {
        db.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(db.Partition.TenantId, db.Partition.DatabaseId,
                new(Collection, ResourceKind.Collection, db.Partition.TransactionDomainId))).Get<ResourceDefinition>();
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, db.Partition, [new PutDocument(Collection, Id, Json, 0)]);
        var operation = new ReplicatedOperation(id, OperationKind.Batch, Root, TimeProvider.System.GetUtcNow(),
            System.Text.Encoding.UTF8.GetString(JsonDefaults.Serialize(command)));
        var discoveryGate = new ReplicaTransportFailureGate("Original discovery cancellation callback failure.");
        var endpointGate = new ReplicaTransportFailureGate("Original endpoint cancellation callback failure.");
        var observed = new Settlement();
        var failures = new List<Exception>();
        await CaptureExpectedAsync(async () =>
        {
            await using var endpoint = new ReplicaLifecycleFixture(configuration: runtime.Node(RequestCqrsCohortScenario.LocalVoter).Journal.Configuration);
            var state = State(runtime);
            await CaptureExpectedAsync(async () =>
            {
                await using var native = Native(runtime, state, discoveryGate);
                await RequestCqrsCohortCleanup.CaptureAsync(() => FailAsync(runtime, db, operation,
                    endpoint, state, native, discoveryGate, endpointGate, observed, token), failures);
                discoveryGate.Release();
                endpointGate.Release();
                await JoinAttemptsAsync(observed, failures);
            }, () => observed.NativeFailure, failures);
        }, () => observed.EndpointFailure, failures);
        RequestCqrsCohortCleanup.ThrowIfAny(failures);
        await ReplicaTransportHealthyContinuation.RunAsync(runtime, db, operation, token);
    }

    private static async Task FailAsync(RequestCqrsCohortRuntimeFixture runtime, TestDatabase db,
        ReplicatedOperation operation, ReplicaLifecycleFixture endpoint, ReplicaSiloDiscoveryState state,
        ReplicaNativeDiscoveryRuntime native, ReplicaTransportFailureGate discoveryGate,
        ReplicaTransportFailureGate endpointGate, Settlement observed, CancellationToken token)
    {
        endpoint.Consensus.AttachTransport(runtime.LocalServices.GetRequiredService<ReplicaGrainServiceClient>());
        endpoint.Consensus.ConfigureForwarding((input, cancellation) => endpointGate.ForwardAsync(db.Database, input, cancellation));
        state.MarkTransportReady();
        var request = Request(runtime, state);
        observed.NativeAttempt = native.DiscoverAsync(request, token);
        observed.EndpointAttempt = endpoint.Consensus.HandleAsync(ReplicaRpc.Forward, ReplicaProtocolCodec.Serialize(operation), token);
        await Task.WhenAll(discoveryGate.Entered.Task, endpointGate.Entered.Task).WaitAsync(token);
        var image = QueueWholeFlowStorage.Bytes(db.Store);
        var cut = db.Store.Position;
        var lifecycle = new ReplicaTransportObservedLifecycle(runtime.LocalServices.GetRequiredService<ILogger<SiloLifecycleSubject>>());
        new ReplicaTransportLifecycle(endpoint.Consensus, state, native).Participate(lifecycle);
        await lifecycle.StartAsync(token);
        observed.FrameworkStop = lifecycle.StopAsync(CancellationToken.None);
        await discoveryGate.Cancelled.Task.WaitAsync(token);
        var original = await lifecycle.StopObserved.Task.WaitAsync(token);
        await Assert.That(original.IsCompleted).IsFalse();
        await Assert.That(endpointGate.Cancelled.Task.IsCompleted).IsFalse();
        discoveryGate.Release();
        await endpointGate.Cancelled.Task.WaitAsync(token);
        await Assert.That(discoveryGate.Exited.Task.IsCompleted).IsTrue();
        await Assert.That(original.IsCompleted).IsFalse();
        endpointGate.Release();
        var aggregate = await Assert.ThrowsExactlyAsync<AggregateException>(() => original.WaitAsync(token))
            ?? throw new InvalidOperationException("The original aggregate is absent.");
        observed.NativeFailure = await RequireFailureAsync(native.StopAsync(), discoveryGate.Failure, token);
        observed.EndpointFailure = await RequireFailureAsync(endpoint.Consensus.StopAsync(CancellationToken.None), endpointGate.Failure, token);
        await Assert.That(aggregate.InnerExceptions.Count).IsEqualTo(ShutdownFailures);
        await Assert.That(ReferenceEquals(aggregate.InnerExceptions[DiscoveryFailureIndex], observed.NativeFailure)).IsTrue();
        await Assert.That(ReferenceEquals(aggregate.InnerExceptions[EndpointFailureIndex], observed.EndpointFailure)).IsTrue();
        observed.TransportFailure = aggregate;
        var reply = runtime.Node(RequestCqrsCohortScenario.FirstRemote).Authentication.VerifyNativeDiscoveryReply(
            request, await observed.NativeAttempt.WaitAsync(token));
        await Assert.That(reply.Error).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(reply.Payload.IsEmpty).IsTrue();
        await Assert.ThrowsAsync<OperationCanceledException>(() => observed.EndpointAttempt.WaitAsync(token));
        observed.EndpointCancellationObserved = true;
        await UnchangedAsync(db, image, cut);
    }

    private static async Task JoinAttemptsAsync(Settlement observed, List<Exception> failures)
    {
        if (observed.FrameworkStop is { } stopped)
            await CaptureExpectedAsync(() => stopped, () => observed.TransportFailure, failures);
        if (observed.NativeAttempt is { } native)
            await RequestCqrsCohortCleanup.CaptureAsync(() => native, failures);
        if (observed.EndpointAttempt is { } endpoint)
            await RequestCqrsCohortCleanup.CaptureAsync(async () =>
            {
                try { await endpoint; }
                catch (OperationCanceledException) when (observed.EndpointCancellationObserved) { }
            }, failures);
    }

    internal static ReplicaSiloDiscoveryState State(RequestCqrsCohortRuntimeFixture runtime)
    {
        var node = runtime.Node(RequestCqrsCohortScenario.LocalVoter);
        return new(UnitExecutionOptions.ReplicaConfiguration(node.Journal.Configuration),
            UnitRoutingOptions.Peers(node.Journal.Configuration, node.Peers), runtime.LocalSilo, StoreReaderContract.RuntimeJournal);
    }

    internal static ReplicaNativeDiscoveryRuntime Native(RequestCqrsCohortRuntimeFixture runtime,
        ReplicaSiloDiscoveryState state, IReplicaNativeDiscoveryObserver? observer = null)
    {
        var node = runtime.Node(RequestCqrsCohortScenario.LocalVoter);
        return new(UnitExecutionOptions.ReplicaConfiguration(node.Journal.Configuration), state, node.Authentication,
            runtime.LocalServices.GetRequiredService<ISiloStatusOracle>(), UnitRoutingOptions.Membership(), TimeProvider.System, observer);
    }

    internal static ReplicaNativeDiscoveryRequest Request(RequestCqrsCohortRuntimeFixture runtime, ReplicaSiloDiscoveryState state)
        => runtime.Node(RequestCqrsCohortScenario.FirstRemote).Authentication.CreateNativeDiscoveryRequest(
            RequestCqrsCohortScenario.LocalVoter, state.RuntimeAddress);

    private static async Task<Exception> RequireFailureAsync(Task stop, Exception original, CancellationToken token)
    {
        var error = await Assert.ThrowsExactlyAsync<AggregateException>(() => stop.WaitAsync(token))
            ?? throw new InvalidOperationException("The native shutdown failure is absent.");
        var causes = error.Flatten().InnerExceptions;
        await Assert.That(causes.Any(value => ReferenceEquals(value, original))).IsTrue();
        await Assert.That(causes.Count).IsEqualTo(SingleCallbackFailure);
        await Assert.That(ReferenceEquals(causes[OriginalCallbackIndex], original)).IsTrue();
        return error;
    }

    internal static async Task UnchangedAsync(TestDatabase db, string[] image, long cut)
    {
        await Assert.That(db.Store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(db.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
    }

    private static Task CaptureExpectedAsync(Func<Task> close, Func<Exception?> expected, List<Exception> failures)
        => RequestCqrsCohortCleanup.CaptureAsync(async () =>
        {
            try { await close(); }
            catch (Exception error) when (ReferenceEquals(error, expected())) { }
        }, failures);

    private sealed class Settlement
    {
        internal Task<ReplicaNativeDiscoveryReply>? NativeAttempt { get; set; }
        internal Task? EndpointAttempt { get; set; }
        internal Task? FrameworkStop { get; set; }
        internal Exception? NativeFailure { get; set; }
        internal Exception? EndpointFailure { get; set; }
        internal Exception? TransportFailure { get; set; }
        internal bool EndpointCancellationObserved { get; set; }
    }
}
