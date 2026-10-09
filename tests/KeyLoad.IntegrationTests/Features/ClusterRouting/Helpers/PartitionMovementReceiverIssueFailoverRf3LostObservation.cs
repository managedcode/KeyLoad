using KeyLoad.Client;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Restored receivers may reveal original issuance but cannot dispatch an unknown original StagePage again.</summary>
internal static class PartitionMovementReceiverIssueFailoverRf3LostObservation
{
    internal static async Task RequireAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        Result<PartitionMoveResult> failed, CancellationToken cancellationToken)
    {
        await Assert.That(failed.IsFailed).IsTrue();
        await Assert.That(failed.Value).IsNull();
        await Assert.That(failed.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.UnknownWriteOutcome));
        // Consume the exact three existing kill receipts before the shared six-stop/cold owner helper.
        // The settled failed ingress cannot dispatch or observe while these resources restart.
        foreach (var node in TwoRf3MembershipProtocol.Nodes.Skip(TwoRf3MembershipProtocol.MembersPerGroup))
        { await wave.RemoteRuntime.RestartAsync(node, cancellationToken).ConfigureAwait(false); }
        var originalId = PartitionMovementReceiverIssueFailoverRf3NativeCut.StageId(seed);
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            originalId, cancellationToken).ConfigureAwait(false);
        var original = PartitionMovementReceiverIssueFailoverRf3NativeCut.Pending(before);
        await PartitionMovementReceiverIssueFailoverRf3NativeCut.RequireUnknownAsync(before, original, observed: false);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
        var resume = await seed.Source.MovePartitionAsync(seed.FirstRequest with { Mode = PartitionMoveMode.Resume },
            cancellationToken).ConfigureAwait(false);
        await Assert.That(resume.IsFailed).IsTrue();
        await Assert.That(resume.Value).IsNull();
        await Assert.That(resume.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.RecoveryRequired));
        await Assert.That(resume.Problem?.Detail).IsEqualTo(PartitionMovementProtocol.Unavailable);
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            originalId, cancellationToken).ConfigureAwait(false);
        await PartitionMovementReceiverIssueFailoverRf3NativeCut.RequireObservationOnlyAsync(original, before, after);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
        var aborted = await PartitionMovementTransferCloseRf3AbortAssertions.AbortAsync(seed,
            cancellationToken).ConfigureAwait(false);
        var terminal = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            originalId, cancellationToken).ConfigureAwait(false);
        await PartitionMovementReceiverIssueFailoverRf3NativeCut.RequireAbortDispositionAsync(terminal, original, aborted);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
        await PartitionMovementTransferCloseRf3Healthy.RequireAsync(wave, seed, aborted,
            cancellationToken).ConfigureAwait(false);
    }
}
