using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ControlledPartitionMovementNativeRetireTrial
{
    private const long InitialCleanup = 0;
    private const long NextCleanup = 1;
    private static readonly Guid MoveId = Guid.Parse("6d5f05d5-d486-4a9a-a02d-62c9911d39e6");
    private readonly ControlledPartitionMovementNativeRetireBorrow borrow = new();
    private ControlledPartitionMovementNativeHosts? owners;
    private Task<GrainOperationReply>? producer;
    private CancellationTokenSource? selectedCaller;

    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        var trial = new ControlledPartitionMovementNativeRetireTrial();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => trial.ExecuteAsync(cancellationToken), failures);
        if (failures.Count != 0)
        { trial.owners?.RetainRoots(); }
        await trial.CloseAsync(failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        owners = await ControlledPartitionMovementNativeHosts.CreateAsync(borrow.ForOwner, cancellationToken);
        await ControlledPartitionMovementNativeRegistration.VerifyAsync(owners);
        var seeded = await ControlledPartitionMovementNativeSeed.ExecuteAsync(owners.Source, cancellationToken);
        var entry = ControlledPartitionMovementNativeEntryReader.Read(owners.Source,
            ControlledPartitionMovementCorpus.SeedId, cancellationToken);
        var seedOperation = entry.Operation ?? throw new InvalidOperationException("The original native seed is absent.");
        var receipt = await ControlledPartitionMovementNativeSeed.ReceiptAsync(owners.Source, seeded, entry);
        var blob = await ControlledPartitionMovementNativeBlob.PublishAsync(owners.Source, cancellationToken);
        var placement = owners.Source.Database.ReadAtomicPartitionPlacement(PhysicalShardCatalogFixture.RootPrincipalId,
            new(1, ControlledPartitionMovementCorpus.Partition));
        var request = new PartitionMoveRequest(MoveId, ControlledPartitionMovementCorpus.Partition,
            owners.Destination.Owner.PhysicalShardId, placement.Revision, PartitionMoveMode.Transfer);
        var first = borrow.Arm();
        selectedCaller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        producer = ControlledPartitionMovementNativeRetireProducer.Start(owners, request, selectedCaller.Token);
        var original = await ControlledPartitionMovementNativeRetireProducer.CaptureAsync(first, producer, cancellationToken);
        var expected = await ControlledPartitionMovementNativeRetireAssertions.PendingAsync(owners,
            original, request, InitialCleanup, cancellationToken);
        var originalBytes = original.Operation.NativePayload.ToArray();
        var cut = ControlledPartitionMovementNativeRetireAssertions.Cut(owners);
        await selectedCaller.CancelAsync();
        await ControlledPartitionMovementNativeRetireProducer.FailedAsync(producer);
        producer = null;
        await borrow.JoinAsync((int)InitialCleanup, cancellationToken);
        var admission = NativeCommandPayload.Read<PartitionMovePhaseCommand>(original.Operation).ReceiverEffectAdmission
            ?? throw new InvalidOperationException("The authentic original Retire authority is absent.");
        await Assert.That(owners.Source.Clock.GetUtcNow() < admission.OriginalExpiresAt).IsTrue();
        await Assert.That(original.Operation.NativePayload.Span.SequenceEqual(originalBytes)).IsTrue();
        await ControlledPartitionMovementNativeRetireAssertions.UnchangedAsync(owners, cut);
        await ControlledPartitionMovementNativeRetireAssertions.PendingAsync(owners, original, request, InitialCleanup, cancellationToken);
        await ControlledPartitionMovementNativeModels.ReadAsync(owners.Target, seedOperation.EvaluatedAt, cancellationToken);
        await ControlledPartitionMovementNativeBlobAssertions.ReadAsync(owners.Target, blob.Expected.Value, cancellationToken);
        await ControlledPartitionMovementNativeRetireProducer.ExpiredAsync(owners, original, cancellationToken);
        var second = borrow.Arm();
        producer = ControlledPartitionMovementNativeRetireProducer.Start(owners,
            request with { Mode = PartitionMoveMode.Resume }, cancellationToken);
        var next = await ControlledPartitionMovementNativeRetireProducer.CaptureAsync(second, producer, cancellationToken);
        var nextExpected = await ControlledPartitionMovementNativeRetireAssertions.PendingAsync(owners,
            next, request, NextCleanup, cancellationToken);
        await Assert.That(JsonDefaults.Serialize(nextExpected).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        var retained = ControlledPartitionMovementNativeRetireRetained.Read(owners, next, request);
        await owners.StopAsync(cancellationToken);
        await ControlledPartitionMovementNativeRetireProducer.FailedAsync(producer);
        producer = null;
        await borrow.JoinAsync((int)NextCleanup, cancellationToken);
        await ControlledPartitionMovementNativeRetireContinuation.ExecuteAsync(owners, next, request,
            expected, retained, receipt, seedOperation.EvaluatedAt, blob, cancellationToken);
    }

    private async Task CloseAsync(List<Exception> failures)
    {
        if (owners is not { } owned)
        { return; }
        if (selectedCaller is { } selected)
        { await ServerFailureObserver.ObserveAsync(selected.CancelAsync, failures); }
        await ServerFailureObserver.ObserveAsync(() => owned.StopAsync(CancellationToken.None), failures);
        if (producer is { } original)
        { await ServerFailureObserver.ObserveAsync(async () => { _ = await original.ConfigureAwait(false); }, failures); }
        await ServerFailureObserver.ObserveAsync(() => borrow.JoinAdmittedAsync(CancellationToken.None), failures);
        if (selectedCaller is { } originalCaller)
        { ServerFailureObserver.Observe(originalCaller.Dispose, failures); }
        if (failures.Count != 0)
        { owned.RetainRoots(); }
        await ServerFailureObserver.ObserveAsync(() => owned.DisposeAsync().AsTask(), failures);
    }
}
