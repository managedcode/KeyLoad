using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Owns one original two-RF3 wave, fault cohort and same-move intact-authority continuation.</summary>
internal static class PartitionMovementCapturePointerRf3Trial
{
    internal static async Task RunAsync(PartitionMovementCapturePointerFault fault, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var failures = new List<Exception>();
        TwoRf3MembershipWave? wave = null;
        PartitionMovementPublicParentRf3Seed? seed = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(caller.Token).ConfigureAwait(false);
            seed = await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, caller.Token).ConfigureAwait(false);
            await ExecuteAsync(wave, seed, fault, caller.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (seed is { } calls)
        { await ServerFailureObserver.ObserveAsync(() => calls.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (wave is { } resources)
        { await ServerFailureObserver.ObserveAsync(() => resources.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementCapturePointerFault fault, CancellationToken cancellationToken)
    {
        var advance = await PartitionMovementCapturePointerRf3Producer.HoldAndJoinAsync(wave, seed, cancellationToken);
        var original = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            advance, cancellationToken).ConfigureAwait(false);
        await PartitionMovementCapturePointerRf3Cut.RequireOriginalAsync(original, seed.FirstRequest, advance);
        var snapshot = await PartitionMovementStoppedNativeSnapshot.CaptureAsync(wave, original, cancellationToken);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            PartitionMovementCapturePointerRf3Fault.Apply(wave, original, fault, advance);
            var before = TwoRf3MembershipProtocol.Nodes.Select(node => PartitionMovementPublicParentRf3Cut.ReadStopped(
                wave, node, seed.FirstRequest, advance)).ToArray();
            await PartitionMovementCapturePointerRf3Fault.RequireHeaderOnlyAsync(original, before, fault, advance);
            await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken);
            await RefuseAsync(seed, cancellationToken);
            var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
                advance, cancellationToken);
            await PartitionMovementCapturePointerRf3Cut.RequireNoForwardEffectAsync(wave, seed.FirstRequest, advance, before, after);
        }, failures).ConfigureAwait(false);
        // Restoration is attempted even after startup/refusal failure; that original failure remains in the ledger.
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var stopped = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
                advance, cancellationToken);
            await snapshot.RestoreAsync(wave, stopped, cancellationToken);
            var restored = TwoRf3MembershipProtocol.Nodes.Select(node => PartitionMovementPublicParentRf3Cut.ReadStopped(
                wave, node, seed.FirstRequest, advance)).ToArray();
            await SameCutAsync(original, restored);
            await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken);
            await HealthyAsync(wave, seed, cancellationToken);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task SameCutAsync(PartitionMovementPublicParentRf3NativeCut[] before,
        PartitionMovementPublicParentRf3NativeCut[] after)
    {
        foreach (var original in before)
        {
            var actual = after.Single(cut => cut.Node == original.Node);
            await PartitionMovementCapturePointerRf3Cut.RequireExactRemainingRowsAsync(original, actual, new(StringComparer.Ordinal));
            await Assert.That(actual.StorePosition).IsEqualTo(original.StorePosition);
        }
    }

    private static async Task RefuseAsync(PartitionMovementPublicParentRf3Seed seed, CancellationToken cancellationToken)
    {
        var resume = seed.FirstRequest with { Mode = PartitionMoveMode.Resume };
        var refused = await seed.Source.MovePartitionAsync(resume, cancellationToken).ConfigureAwait(false);
        await Assert.That(refused.IsFailed).IsTrue();
        await Assert.That(refused.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.RecoveryRequired));
        await McpCallerAssertions.ErrorAsync(await seed.Official.CallAsync(PartitionMovePublicProtocol.ToolName,
            resume, cancellationToken).ConfigureAwait(false), ErrorCode.RecoveryRequired, dispatched: true);
    }

    private static async Task HealthyAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        CancellationToken cancellationToken)
    {
        var resume = seed.FirstRequest with { Mode = PartitionMoveMode.Resume };
        var terminal = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.MovePartitionAsync(resume,
            cancellationToken).ConfigureAwait(false));
        await PartitionMovementPublicParentRf3Scenario.RequireTerminalAsync(seed, seed.FirstRequest, seed.OriginalPlacement,
            seed.Directory.ControlOwner, terminal, cancellationToken).ConfigureAwait(false);
        await seed.VerifyAsync(cancellationToken).ConfigureAwait(false);
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, cancellationToken);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(before);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken);
        await PartitionMovementPublicCallerReplay.RequireAsync(seed.Source, seed.Official, seed.FirstRequest, terminal,
            cancellationToken).ConfigureAwait(false);
        await seed.VerifyAsync(cancellationToken).ConfigureAwait(false);
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, cancellationToken);
        await SameCutAsync(before, after);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken);
    }
}
