using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Actual native WAL calibration never substitutes for the separately executed legal and encoded-refusal cohorts.</summary>
internal static class PartitionMovementFinalInstallFrameRf3Trial
{
    private const int OneByte = 1;

    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        TwoRf3MembershipWave? wave = null;
        PartitionMovementPublicParentRf3Seed? seed = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var originalCap = new ZoneTreeStorageExecutionOptions().MaxFrameBytes;
            wave = await TwoRf3MembershipWave.StartProtectedFramesAsync(originalCap, caller.Token);
            seed = await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, caller.Token);
            await ExecuteAsync(wave, seed, originalCap, caller.Token);
        }, failures).ConfigureAwait(false);
        if (seed is { } clients)
        { await ServerFailureObserver.ObserveAsync(() => clients.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (wave is { } resources)
        { await ServerFailureObserver.ObserveAsync(() => resources.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        int originalCap, CancellationToken cancellationToken)
    {
        var precut = await PartitionMovementFinalInstallFrameRf3Preparation.CaptureAsync(wave, seed, cancellationToken);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken);
        _ = await PartitionMovementFinalInstallFrameRf3Producer.ResumeFinalAsync(wave, seed, null, cancellationToken);
        var calibration = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            precut.EffectId, cancellationToken);
        var frames = await ReadFinalFramesAsync(wave, seed, precut, calibration, originalCap, cancellationToken);
        var exact = frames.First().PayloadBytes;
        foreach (var frame in frames)
        {
            await Assert.That(frame.Installed).IsTrue();
            await Assert.That(frame.PayloadBytes).IsEqualTo(exact);
            await Assert.That(frame.RawMutationBytes).IsLessThanOrEqualTo((long)exact - OneByte);
        }
        await precut.Calibrated.RestoreAsync(wave, calibration, cancellationToken);
        await PartitionMovementParentOperationalCapacityRf3Assertions.RequireRestoredAsync(wave, seed, precut.Cuts);
        await RequireHistoryAsync(wave, seed, precut, originalCap, checked(exact - OneByte), cancellationToken);
        await DenyAndRestoreAsync(wave, seed, precut, exact, cancellationToken);
    }

    private static async Task DenyAndRestoreAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementFinalInstallFramePrecut precut, int exact, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        PartitionMovementPublicParentRf3NativeCut[]? joinedRefusal = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var deniedCap = checked(exact - OneByte);
            await wave.ReconfigureMovementFrameAsync(deniedCap, cancellationToken);
            _ = await PartitionMovementFinalInstallFrameRf3Producer.ResumeFinalAsync(wave, seed,
                ErrorCode.ResourceExhausted, cancellationToken);
            var refused = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
                precut.EffectId, cancellationToken);
            joinedRefusal = refused;
            await PartitionMovementFinalInstallFrameRf3Assertions.RequireDeniedAsync(wave, seed, precut, refused);
            var frames = await ReadFinalFramesAsync(wave, seed, precut, refused, deniedCap, cancellationToken);
            foreach (var frame in frames)
            {
                await Assert.That(frame.EncodedFrameLimitRejected).IsTrue();
                await Assert.That(frame.Installed).IsFalse();
            }
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var stopped = joinedRefusal ?? await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
                precut.EffectId, cancellationToken);
            await precut.Refused.RestoreAsync(wave, stopped, cancellationToken);
            await PartitionMovementParentOperationalCapacityRf3Assertions.RequireRestoredAsync(wave, seed, precut.Cuts);
            await wave.ReconfigureMovementFrameAsync(exact, cancellationToken);
            var fresh = TimeProvider.System.GetUtcNow();
            var terminal = await PartitionMovementFinalInstallFrameRf3Producer.ResumeFinalAsync(wave, seed,
                null, cancellationToken) ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
            await PartitionMovementFinalInstallFrameRf3Healthy.RequireAsync(wave, seed, precut, terminal, fresh,
                exact, cancellationToken);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task<NativeInstallFrameInspectionReceipt[]> ReadFinalFramesAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, PartitionMovementFinalInstallFramePrecut precut,
        PartitionMovementPublicParentRf3NativeCut[] cuts, int actualCap, CancellationToken cancellationToken)
    {
        var frames = new List<NativeInstallFrameInspectionReceipt>();
        foreach (var cut in cuts.Where(cut => cut.Header is null))
        {
            var issued = cut.OriginalReceiverIssuance ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
            await Assert.That(issued.OriginalPhaseCommandId).IsEqualTo(precut.EffectId);
            frames.Add(await PartitionMovementFinalInstallFrameRf3Inspection.ReadAsync(wave, cut.Node,
                precut.Owners[cut.Node], seed.Partition, issued.ReceiverPrincipalId, precut.EffectId,
                actualCap, cancellationToken));
        }
        await Assert.That(frames.Count).IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
        return frames.ToArray();
    }

    private static async Task RequireHistoryAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementFinalInstallFramePrecut precut, int originalCap, int deniedCap, CancellationToken cancellationToken)
    {
        var last = precut.State.LastIssued!;
        var priorGrant = last.OriginalResult!.Get<PartitionMovePhaseResult>().Grant!;
        foreach (var cut in precut.Cuts)
        {
            var control = cut.Header is not null;
            var principal = control ? precut.State.Header!.OperatorPrincipalId : PartitionStoreProtocol.AdministratorId;
            var command = control ? last.OriginalPhaseCommandId : priorGrant.PhaseCommandId;
            var observed = await PartitionMovementFinalInstallFrameRf3Inspection.ReadAsync(wave, cut.Node,
                precut.Owners[cut.Node], seed.Partition, principal, command, originalCap, cancellationToken);
            await Assert.That(observed.MaximumObservedPayloadBytes).IsLessThanOrEqualTo(deniedCap);
        }
    }
}
