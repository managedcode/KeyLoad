using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Every transferred business family remains literal; real proof/quota metadata is checked separately.</summary>
internal static class PartitionMovementActiveAdjunctRf3Assertions
{
    internal static async Task RequireBusinessUnchangedAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementPublicParentRf3NativeCut[] before, PartitionMovementPublicParentRf3NativeCut[] after)
    {
        foreach (var previous in before)
        {
            var current = after.Single(cut => cut.Node == previous.Node);
            foreach (var family in PartitionRecordFamilies.All.Where(family => family is not
                (PartitionRecordFamilies.OutcomeV2 or PartitionRecordFamilies.OutcomeLocatorV2 or PartitionRecordFamilies.OutcomeLocator)))
            {
                var prefix = Convert.ToHexString(KeySpace.Partition(family, seed.Partition));
                await SqlRf3Protocol.EqualAsync(previous.Rows.Where(row => row.StartsWith(prefix, StringComparison.Ordinal)).ToArray(),
                    current.Rows.Where(row => row.StartsWith(prefix, StringComparison.Ordinal)).ToArray());
            }
        }
        var original = PartitionMovementReceiverIssueFailoverRf3NativeCut.Pending(after);
        await PartitionMovementReceiverIssueFailoverRf3NativeCut.RequireUnknownAsync(after, original, observed: false);
        await Assert.That(after.Any(cut => cut.OriginalReceiverIssuance is not null)).IsTrue();
        await Assert.That(original.OriginalPhase!.PageOrdinal).IsEqualTo(FirstStageOrdinal);
        foreach (var control in after.Where(cut => cut.Header is not null))
        {
            await SqlRf3Protocol.EqualAsync(seed.OriginalPlacement, control.Header!.SourcePlacement);
            await Assert.That(control.Header.OriginalCapturePhaseCommandId).IsNotNull();
            await Assert.That(control.Header.InterruptedOriginalPhaseCommandId).IsNull();
        }
    }
    private const int FirstStageOrdinal = 0;
}
