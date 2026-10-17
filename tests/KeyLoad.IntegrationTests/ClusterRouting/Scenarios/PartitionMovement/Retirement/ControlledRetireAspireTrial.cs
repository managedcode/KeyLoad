using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using ManagedCode.Communication;
using P = KeyLoad.IntegrationTests.Features.ClusterRouting.ControlledRetireAspireProtocol;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class ControlledRetireAspireTrial
{
    private ControlledRetireAspireFixture? fixture;
    private PartitionMovementPublicParentRf3Seed? seed;
    private Task<Result<PartitionMoveResult>>? producer;
    private CancellationTokenSource? caller;

    internal static async Task RunAsync(CancellationToken token)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var parent = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        var trial = new ControlledRetireAspireTrial();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => trial.ExecuteAsync(parent.Token), failures);
        if (failures.Count != P.First)
        {
            trial.fixture?.RetainRoots();
        }
        await trial.CloseAsync(failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task ExecuteAsync(CancellationToken token)
    {
        fixture = await ControlledRetireAspireFixture.StartAsync(token);
        seed = await PartitionMovementPublicParentRf3Seed.CreateAsync(fixture.Wave, token);
        var state = await ControlledRetireAspireSeed.CreateAsync(seed, token);
        var firstArm = fixture.Arm(seed.FirstRequest, P.InitialCleanup);
        caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        producer = seed.Source.MovePartitionAsync(seed.FirstRequest, caller.Token);
        await fixture.WaitSealedAsync(firstArm, producer, token);
        var cuts = await ControlledRetireAspireCutClient.ReadAsync(fixture.Wave, firstArm,
            ControlledRetireAspirePublicIds.For(seed.FirstRequest, P.InitialCleanup), token);
        var original = await ControlledRetireAspirePending.RequireAsync(seed, cuts, P.InitialCleanup);
        var before = await ControlledRetireAspireCutClient.ReadAsync(fixture.Wave, firstArm,
            original.Operation.Id, token, original.Expected.InstalledReceipt);
        await ControlledRetireAspirePending.InstallAsync(seed, original, before);
        var seedCuts = await ControlledRetireAspireCutClient.ReadAsync(fixture.Wave, firstArm,
            original.Operation.Id, token, state.Receipt.Token);
        state = await ControlledRetireAspireSeed.RecordedAtAsync(state, seedCuts);
        var blobCuts = await ControlledRetireAspireCutClient.ReadAsync(fixture.Wave, firstArm,
            original.Operation.Id, token, state.Blob.Expected.Receipt.Token);
        state = state with
        {
            Blob = await ControlledRetireAspireBlob.NativeAsync(seed, state.Blob, blobCuts)
        };
        state = await ControlledRetireAspireModels.NativeAsync(seed, state, before);
        await caller.CancelAsync();
        await ControlledRetireAspireProducer.FailedAsync(producer, callerCancelled: true);
        producer = null;
        await fixture.JoinCancelledAsync(firstArm, token);
        var after = await ControlledRetireAspireCutClient.ReadAsync(fixture.Wave, firstArm,
            original.Operation.Id, token, original.Expected.InstalledReceipt);
        await ControlledRetireAspireConservation.RequireAsync(before, after);
        await ControlledRetireAspireProducer.BeforeExpiryAsync(original);
        await ControlledRetireAspirePending.RequireAsync(seed, after, P.InitialCleanup);
        await ControlledRetireAspireSeed.ReplayAsync(seed, state, token);
        await ControlledRetireAspireProducer.ExpiredAsync(original, token);
        caller.Dispose();
        caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        var secondArm = fixture.Arm(seed.FirstRequest, P.NextCleanup);
        producer = seed.Source.MovePartitionAsync(seed.FirstRequest with
        {
            Mode = PartitionMoveMode.Resume
        }, caller.Token);
        await fixture.WaitSealedAsync(secondArm, producer, token);
        var nextCuts = await ControlledRetireAspireCutClient.ReadAsync(fixture.Wave, secondArm,
            ControlledRetireAspirePublicIds.For(seed.FirstRequest, P.NextCleanup), token);
        var next = await ControlledRetireAspirePending.RequireAsync(seed, nextCuts, P.NextCleanup);
        await SqlRf3Protocol.EqualAsync(original.Expected, next.Expected);
        var stopped = await fixture.StopAndReadAsync(seed.FirstRequest, next.Operation.Id, token);
        await ControlledRetireAspireProducer.FailedAsync(producer, callerCancelled: false);
        producer = null;
        await fixture.JoinCancelledAsync(secondArm, token);
        await ControlledRetireAspirePending.RetainedAsync(next, stopped);
        await ControlledRetireAspireContinuation.ExecuteAsync(fixture, seed, state, next, secondArm, original.Expected, token);
    }

    private async Task CloseAsync(List<Exception> failures)
    {
        if (caller is { } selected)
        {
            await ServerFailureObserver.ObserveAsync(selected.CancelAsync, failures);
        }
        if (producer is { } pending)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                _ = await pending;
            }, failures);
        }
        if (seed is { } clients)
        {
            await ServerFailureObserver.ObserveAsync(() => clients.DisposeAsync().AsTask(), failures);
        }
        if (fixture is { } owned)
        {
            if (failures.Count != P.First)
            {
                owned.RetainRoots();
            }
            await ServerFailureObserver.ObserveAsync(() => owned.DisposeAsync().AsTask(), failures);
        }
        if (caller is { } original)
        {
            ServerFailureObserver.Observe(original.Dispose, failures);
        }
    }
}
