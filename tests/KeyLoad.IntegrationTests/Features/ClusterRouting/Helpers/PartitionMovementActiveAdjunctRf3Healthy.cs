using KeyLoad.Client;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Resume observes the genuine first issuance, retains unknown effects, then genuine Abort permits a fresh healthy move.</summary>
internal static class PartitionMovementActiveAdjunctRf3Healthy
{
    internal static async Task RequireAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementPublicParentRf3NativeCut[] before, CancellationToken cancellationToken)
    {
        var originalId = PartitionMovementReceiverIssueFailoverRf3NativeCut.StageId(seed);
        var original = PartitionMovementReceiverIssueFailoverRf3NativeCut.Pending(before);
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
