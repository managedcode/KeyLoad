using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Verifies genuine cold retained page compaction without changing native receipt or proof requirements.</summary>
internal static class PartitionMovementPublicParentRf3CompactionAssertions
{
    internal static async Task RequireAsync(PartitionMovementPublicParentRf3NativeCut[] cuts)
    {
        var control = cuts.Where(cut => cut.Header is not null).ToArray();
        await Assert.That(control.Length).IsEqualTo(3);
        foreach (var cut in control)
        {
            await Assert.That(cut.SettledStagePages.Length).IsGreaterThan(0);
            foreach (var phase in cut.SettledStagePages)
            {
                await Assert.That(phase.OriginalPhase).IsNull();
                await Assert.That(phase.OriginalReceiverSourceWitness).IsNull();
                await Assert.That(phase.OriginalReceiverIssuePacket).IsNull();
                await Assert.That(phase.OriginalBodyDigest.Length).IsEqualTo(64);
                await Assert.That(phase.OriginalPhaseIdentityDigest.Length).IsEqualTo(64);
                await Assert.That(phase.OriginalResult!.Error).IsNull();
                await Assert.That(phase.OriginalResult.Get<PartitionMovePhaseResult>().Stage)
                    .IsEqualTo(PartitionMovePeerStage.StagePage);
                await Assert.That(phase.OriginalReceiverIssuanceWitness).IsNotNull();
                await Assert.That(phase.ReceiverIssuanceCheckpointReceipt).IsNotNull();
                await Assert.That(phase.ObservationCheckpointReceipt).IsNotNull();
                await Assert.That(phase.OriginalOutcomeWitness).IsNotNull();
            }
        }
    }
}
