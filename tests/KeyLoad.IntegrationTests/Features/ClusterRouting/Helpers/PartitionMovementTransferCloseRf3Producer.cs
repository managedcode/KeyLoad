using KeyLoad.Client;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Only genuine source Page and original ingress Close failure identities escape their owners.</summary>
internal static class PartitionMovementTransferCloseRf3Producer
{
    private const string MissingPage = "The original native Page did not return before the actual producer settled.";
    private const string UnexpectedSuccess = "The original canceled movement unexpectedly succeeded.";

    internal static async Task CancelAfterActualPageAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, CancellationToken cancellationToken)
    {
        var discovery = await PartitionMovementExpiredRetireSealedOperationRf3Trial.ReadDiscoveryAsync(wave,
            cancellationToken).ConfigureAwait(false);
        var pageArm = wave.QueryControls.WriteArm(PartitionMovementPublicParentRf3Administrator.PrincipalId,
            Guid.Empty, GrainReadKind.PartitionMovementTransferData, RequestCqrsProbePhase.TransferPageReturned,
            RequestCqrsProbeAction.Hold);
        var closeArm = wave.QueryControls.WriteArm(PartitionMovementPublicParentRf3Administrator.PrincipalId,
            seed.FirstRequest.MoveId, null, RequestCqrsProbePhase.ParentTransferCloseFailed,
            RequestCqrsProbeAction.Hold);
        using var originalCaller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var observedPage = wave.QueryControls.WaitForMarkerAsync(pageArm, RequestCqrsProbePhase.TransferPageReturned,
            RequestCqrsProbeOutcome.Observed, discovery, cancellationToken);
        var call = seed.Source.MovePartitionAsync(seed.FirstRequest, originalCaller.Token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            _ = await Task.WhenAny(observedPage, call).ConfigureAwait(false);
            if (!observedPage.IsCompletedSuccessfully)
            { throw new InvalidOperationException(MissingPage); }
            var page = await observedPage.ConfigureAwait(false);
            await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(page, pageArm, Guid.Empty,
                RequestCqrsProbePhase.TransferPageReturned, discovery).ConfigureAwait(false);
            await originalCaller.CancelAsync().ConfigureAwait(false);
            var close = await wave.QueryControls.WaitForMarkerAsync(closeArm,
                RequestCqrsProbePhase.ParentTransferCloseFailed, RequestCqrsProbeOutcome.Observed,
                discovery, cancellationToken).ConfigureAwait(false);
            await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(close, closeArm, seed.FirstRequest.MoveId,
                RequestCqrsProbePhase.ParentTransferCloseFailed, discovery).ConfigureAwait(false);
            await RequireCancelledAsync(wave, pageArm, page.RequestId, Guid.Empty,
                RequestCqrsProbePhase.TransferPageReturned, discovery, cancellationToken).ConfigureAwait(false);
            await RequireCancelledAsync(wave, closeArm, close.RequestId, seed.FirstRequest.MoveId,
                RequestCqrsProbePhase.ParentTransferCloseFailed, discovery, cancellationToken).ConfigureAwait(false);
            await RequireCallerCancelledAsync(call, originalCaller.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(originalCaller.CancelAsync, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => RequireCallerCancelledAsync(call, originalCaller.Token), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () => { _ = await observedPage.ConfigureAwait(false); }, failures).ConfigureAwait(false);
        await JoinArmAsync(wave, pageArm, discovery, failures, cancellationToken).ConfigureAwait(false);
        await JoinArmAsync(wave, closeArm, discovery, failures, cancellationToken).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RequireCallerCancelledAsync(Task<Result<PartitionMoveResult>> call,
        CancellationToken originalCaller)
    {
        try
        {
            var actual = await call.ConfigureAwait(false);
            await Assert.That(actual.IsFailed).IsTrue();
            await Assert.That(actual.Value).IsNull();
            await Assert.That(actual.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Cancelled));
        }
        catch (OperationCanceledException) when (originalCaller.IsCancellationRequested) { return; }
        if (!originalCaller.IsCancellationRequested)
        { throw new InvalidOperationException(UnexpectedSuccess); }
    }

    private static async Task RequireCancelledAsync(TwoRf3MembershipWave wave, Guid arm, Guid request,
        Guid command, RequestCqrsProbePhase phase, IReadOnlyList<ReplicaSiloDiscovery> discovery,
        CancellationToken cancellationToken)
    {
        var canceled = await wave.QueryControls.WaitForMarkerAsync(arm, phase,
            RequestCqrsProbeOutcome.Cancelled, discovery, cancellationToken).ConfigureAwait(false);
        await Assert.That(canceled.RequestId).IsEqualTo(request);
        await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(wave.QueryControls, arm, request, command,
            discovery, cancellationToken).ConfigureAwait(false);
    }

    private static async Task JoinArmAsync(TwoRf3MembershipWave wave, Guid arm,
        IReadOnlyList<ReplicaSiloDiscovery> discovery, List<Exception> failures, CancellationToken cancellationToken)
    {
        if (wave.QueryControls.ArmFor(arm).RequestId is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => wave.QueryControls.WaitForSettlementAsync(arm,
                discovery, cancellationToken), failures).ConfigureAwait(false);
        }
        await ServerFailureObserver.ObserveAsync(() => wave.QueryControls.RetireArmAsync(arm,
            cancellationToken), failures).ConfigureAwait(false);
    }
}
