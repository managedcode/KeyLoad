using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeRequestDrainLifecycleOperation
{
    internal static async Task SeedAsync(NativeRequestDrainLifecycleState state)
    {
        state.Database.Configure(NativeRequestDrainLifecycleProtocol.Collection, ResourceKind.Collection,
            state.Database.Partition.TransactionDomainId);
        var before = state.Database.ReadCanonicalCut().AppliedPosition;
        state.Receipt = state.Database.Submit(OperationKind.Batch, state.Original, id: state.Original.CommandId)
            .Get<CommitReceipt>();
        var expected = new CommitReceipt(state.Original.CommandId, new(state.Database.Store.Identity.Incarnation, state.Database.Partition.AtomicPartitionId, before + 1L), [new("putDocument", "native-request-drain", "original", 1L)], DurabilityProfile.ProcessDurable, 1);
        await EqualAsync(state.Receipt, expected);
        await AppliedAsync(state, state.Original.CommandId, before + 1L);
        await Assert.That(state.Receipt.Mutations.Single().CompositionReferences.IsEmpty).IsTrue();
        state.Image = QueueWholeFlowStorage.Bytes(state.Database.Store);
        state.Position = state.Database.Store.Position;
    }

    internal static async Task ProduceAsync(NativeRequestDrainLifecycleState state)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var requestId = Guid.NewGuid();
            using var producer = state.Requests.Acquire(requestId, NativeRequestWorkKind.RequestProducer);
            using var capability = state.Requests.Acquire(requestId, NativeRequestWorkKind.ReadCapability);
            await ServerFailureObserver.ObserveAsync(() => ProduceOwnedAsync(state), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ProduceOwnedAsync(NativeRequestDrainLifecycleState state)
    {
        await OriginalAsync(state);
        state.Entered.TrySetResult();
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, state.Clock, state.Requests.ShutdownToken);
        }
        catch (OperationCanceledException) when (state.Requests.ShutdownToken.IsCancellationRequested)
        {
            state.Canceled.TrySetResult();
        }
        await state.Release.Task.WaitAsync(state.Token);
        await OriginalAsync(state);
    }

    internal static async Task ActiveAsync(NativeRequestDrainLifecycleState state)
    {
        await Assert.That(state.Requests.ShutdownToken.IsCancellationRequested).IsFalse();
        state.Events.Add(NativeRequestDrainLifecycleProtocol.ActiveEvent);
    }

    internal static async Task StorageAsync(NativeRequestDrainLifecycleState state)
    {
        await Assert.That(state.Requests.IsJoined).IsTrue();
        await OriginalAsync(state);
        state.Events.Add(NativeRequestDrainLifecycleProtocol.StorageEvent);
    }

    internal static async Task ClosedAsync(NativeRequestDrainLifecycleState state)
    {
        var original = state.Drain = state.Requests.DrainAsync();
        await Assert.That(ReferenceEquals(original, state.Requests.DrainAsync())).IsTrue();
        await Assert.That(original.IsCompleted).IsFalse();
        var denied = Assert.ThrowsExactly<KeyLoadException>(() =>
            state.Requests.Acquire(Guid.NewGuid(), NativeRequestWorkKind.CommandCapability));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await OriginalAsync(state);
    }

    private static async Task OriginalAsync(NativeRequestDrainLifecycleState state)
    {
        var reference = new EntityRef(state.Database.Partition, NativeRequestDrainLifecycleProtocol.Collection,
            NativeRequestDrainLifecycleProtocol.Document);
        var expected = new DocumentResult(state.Database.ExpectedReference(reference), 1L, "{\"value\":\"original\"}", false, []);
        await EqualAsync(state.Database.Database.GetDocument(NativeRequestDrainLifecycleProtocol.Root, reference,
            minimumToken: state.Receipt.Token), expected);
        await Assert.That(QueueWholeFlowStorage.Bytes(state.Database.Store).SequenceEqual(state.Image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(state.Database.Store.Position).IsEqualTo(state.Position);
    }

    internal static async Task ContinuedAsync(NativeRequestDrainLifecycleState state)
    {
        await Assert.That(state.Requests.IsJoined).IsTrue();
        await EqualAsync(state.Events.ToArray(), new[] { "active-stop", "storage-stop" });
        await OriginalAsync(state);
        var replay = state.Database.Submit(OperationKind.Batch, state.Original, id: state.Original.CommandId).Get<CommitReceipt>();
        await EqualAsync(replay, state.Receipt);
        await Assert.That(replay.Mutations.Single().CompositionReferences.IsEmpty).IsTrue();
        await OriginalAsync(state);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var healthy = new NativeRequestWorkOwner(UnitRoutingOptions.Routing());
            await ServerFailureObserver.ObserveAsync(() => HealthyAsync(state, healthy, failures), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task HealthyAsync(NativeRequestDrainLifecycleState state, NativeRequestWorkOwner healthy,
        List<Exception> failures)
    {
        using var admitted = healthy.Acquire(Guid.NewGuid(), NativeRequestWorkKind.CommandCapability);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var before = state.Database.ReadCanonicalCut().AppliedPosition;
            var command = new CommandRequest(Guid.NewGuid(), state.Database.Partition,
                [new PutDocument(NativeRequestDrainLifecycleProtocol.Collection, NativeRequestDrainLifecycleProtocol.Document,
                    NativeRequestDrainLifecycleProtocol.ContinuedJson, 1L)]);
            var receipt = state.Database.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();
            var expectedReceipt = new CommitReceipt(command.CommandId, new(state.Database.Store.Identity.Incarnation, state.Database.Partition.AtomicPartitionId, before + 1L), [new("putDocument", "native-request-drain", "original", 2L)], DurabilityProfile.ProcessDurable, 1);
            await EqualAsync(receipt, expectedReceipt);
            await AppliedAsync(state, command.CommandId, before + 1L);
            await Assert.That(receipt.Mutations.Single().CompositionReferences.IsEmpty).IsTrue();
            var expected = new DocumentResult(state.Database.ExpectedReference(new(state.Database.Partition, "native-request-drain", "original")),
                2L, "{\"value\":\"continued\"}", false, []);
            await EqualAsync(state.Database.Database.GetDocument(NativeRequestDrainLifecycleProtocol.Root, expected.Reference,
                minimumToken: receipt.Token), expected);
        }, failures);
    }

    private static async Task AppliedAsync(NativeRequestDrainLifecycleState state, Guid commandId, long position)
    {
        var entry = state.Database.ReadStoredOutcome(commandId);
        var cut = state.Database.ReadCanonicalCut();
        await Assert.That(entry.CanonicalSequence).IsEqualTo(position);
        await Assert.That(entry.PreparedCommand!.CommandId).IsEqualTo(commandId);
        await Assert.That(cut.AppliedPosition).IsEqualTo(position);
        await Assert.That(state.Database.Database.LastApplied).IsEqualTo(position);
    }

    internal static async Task DisposeAndVerifyAsync(NativeRequestDrainLifecycleState state)
    {
        var drain = state.Drain ??= state.Requests.DrainAsync();
        if (!state.DrainObserved)
        {
            await ServerFailureObserver.ObserveAsync(() => drain, state.DrainFailures);
            state.DrainObserved = true;
        }
        var disposal = state.Requests.DisposeAsync().AsTask();
        await ServerFailureObserver.ObserveAsync(() => disposal, state.DisposalFailures);
        await ServerFailureObserver.ObserveAsync(() => state.Producer ?? Task.CompletedTask, state.Failures);
        var drainMatches = MatchFailures(state, state.DrainFailures);
        var disposalMatches = MatchFailures(state, state.DisposalFailures);
        await Assert.That(drainMatches && disposalMatches).IsTrue();
        await Assert.That(state.Requests.IsJoined).IsTrue();
        await Assert.That(ReferenceEquals(drain, state.Requests.DrainAsync())).IsTrue();
        await Assert.That(ReferenceEquals(disposal, state.Requests.DisposeAsync().AsTask())).IsTrue();
        await Assert.That(drain.IsFaulted).IsEqualTo(state.ExpectsFailure);
        await Assert.That(disposal.IsFaulted).IsEqualTo(state.ExpectsFailure);
    }

    private static bool MatchFailures(NativeRequestDrainLifecycleState state, List<Exception> observed)
    {
        var leaves = observed.SelectMany(Leaves).ToArray();
        var matches = state.ExpectsFailure
            ? leaves.Length == 1 && ReferenceEquals(leaves.Single(), state.CallbackFailure)
            : leaves.Length == 0;
        if (!matches)
        {
            state.Failures.AddRange(observed);
        }
        return matches;
    }

    private static IEnumerable<Exception> Leaves(Exception failure)
        => failure is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : new[] { failure };

    private static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
