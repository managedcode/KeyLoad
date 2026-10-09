using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Only verified identity leaves the real request owner; the original producer is cancelled and joined.</summary>
internal static class PartitionMovementFinalInstallFrameRf3Producer
{
    private const string MissingPreflight = "The actual parent completed before its required native final Install preflight marker.";
    internal const string Administrator = PartitionMovementPublicParentRf3Administrator.PrincipalId;

    internal static Task HoldPreFinalAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, CancellationToken cancellationToken)
        => HoldAsync(wave, seed, seed.FirstRequest, seed.FirstRequest.MoveId,
            RequestCqrsProbePhase.ParentFinalInstallPreflight, cancellationToken);

    private static async Task HoldAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionMoveRequest request, Guid commandId, RequestCqrsProbePhase phase, CancellationToken cancellationToken)
    {
        var signed = await PartitionMovementExpiredRetireSealedOperationRf3Trial.ReadDiscoveryAsync(wave,
            cancellationToken).ConfigureAwait(false);
        var arm = wave.QueryControls.WriteArm(Administrator, commandId, null, phase, RequestCqrsProbeAction.Hold);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var observed = wave.QueryControls.WaitForMarkerAsync(arm, phase,
            RequestCqrsProbeOutcome.Observed, signed, caller.Token);
        var call = seed.Source.MovePartitionAsync(request, caller.Token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            _ = await Task.WhenAny(observed, call).ConfigureAwait(false);
            if (!observed.IsCompletedSuccessfully)
            {
                var early = await call.ConfigureAwait(false);
                if (early.IsFailed && Enum.TryParse<ErrorCode>(early.Problem?.ErrorCode, out var actualCode)
                    && Enum.IsDefined(actualCode))
                { throw Errors.Fail(actualCode, early.Problem?.Detail ?? MissingPreflight); }
                throw new InvalidOperationException(MissingPreflight);
            }
            var marker = await observed.ConfigureAwait(false);
            await Assert.That(marker.CommandId).IsEqualTo(commandId);
            await Assert.That(marker.RequestId).IsNotEqualTo(Guid.Empty);
            await caller.CancelAsync().ConfigureAwait(false);
            await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(wave.QueryControls, arm, marker.RequestId,
                commandId, signed, cancellationToken).ConfigureAwait(false);
            var actual = await call.ConfigureAwait(false);
            await Assert.That(actual.IsFailed).IsTrue();
            await Assert.That(actual.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.UnknownWriteOutcome));
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(caller.CancelAsync, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => JoinObservationAsync(observed, caller.Token), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => JoinArmAsync(wave, arm, signed, cancellationToken),
            failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () => { _ = await call.ConfigureAwait(false); }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => wave.QueryControls.RetireArmAsync(arm,
            cancellationToken), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task<PartitionMoveResult?> ResumeFinalAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, ErrorCode? expectedError, CancellationToken cancellationToken)
    {
        var signed = await PartitionMovementExpiredRetireSealedOperationRf3Trial.ReadDiscoveryAsync(wave,
            cancellationToken).ConfigureAwait(false);
        var arm = wave.QueryControls.WriteArm(Administrator, seed.FirstRequest.MoveId, null,
            RequestCqrsProbePhase.ParentFinalInstallPreflight, RequestCqrsProbeAction.Hold);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var markerTask = wave.QueryControls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.ParentFinalInstallPreflight,
            RequestCqrsProbeOutcome.Observed, signed, caller.Token);
        var call = seed.Source.MovePartitionAsync(seed.FirstRequest with { Mode = PartitionMoveMode.Resume }, caller.Token);
        var failures = new List<Exception>();
        PartitionMoveResult? terminal = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            _ = await Task.WhenAny(markerTask, call).ConfigureAwait(false);
            if (!markerTask.IsCompletedSuccessfully)
            {
                var early = await call.ConfigureAwait(false);
                await Assert.That(early.Problem?.ErrorCode).IsNull();
                throw new InvalidOperationException(MissingPreflight);
            }
            var marker = await markerTask.ConfigureAwait(false);
            await Assert.That(marker.CommandId).IsEqualTo(seed.FirstRequest.MoveId);
            wave.QueryControls.WriteRelease(arm, marker.RequestId);
            var released = await wave.QueryControls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.ParentFinalInstallPreflight,
                RequestCqrsProbeOutcome.Released, signed, cancellationToken).ConfigureAwait(false);
            await Assert.That(released.RequestId).IsEqualTo(marker.RequestId);
            var actual = await call.ConfigureAwait(false);
            if (expectedError is { } error)
            {
                await Assert.That(actual.IsFailed).IsTrue();
                await Assert.That(actual.Problem?.ErrorCode).IsEqualTo(error.ToString());
            }
            else
            { terminal = await McpCallerAssertions.SdkSuccessAsync(actual); }
            await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(wave.QueryControls, arm, marker.RequestId,
                seed.FirstRequest.MoveId, signed, cancellationToken).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(caller.CancelAsync, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () => { _ = await markerTask.ConfigureAwait(false); }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () => { _ = await call.ConfigureAwait(false); }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => JoinArmAsync(wave, arm, signed, cancellationToken), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => wave.QueryControls.RetireArmAsync(arm,
            cancellationToken), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return terminal;
    }

    private static async Task JoinObservationAsync(Task<RequestCqrsProbeMarkerRecord> observed, CancellationToken ownedWait)
    {
        try
        { _ = await observed.ConfigureAwait(false); }
        catch (OperationCanceledException) when (ownedWait.IsCancellationRequested) { }
    }

    private static Task JoinArmAsync(TwoRf3MembershipWave wave, Guid arm,
        IReadOnlyList<ReplicaSiloDiscovery> signed, CancellationToken cancellationToken)
        => wave.QueryControls.ArmFor(arm).RequestId is not null
            ? wave.QueryControls.WaitForSettlementAsync(arm, signed, cancellationToken) : Task.CompletedTask;

}
