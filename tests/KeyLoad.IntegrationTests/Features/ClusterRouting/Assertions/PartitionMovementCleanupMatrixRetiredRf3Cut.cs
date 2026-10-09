using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>The genuine acknowledged native issuer proof stays observed while the original effect remains unknown.</summary>
internal static class PartitionMovementCleanupMatrixRetiredRf3Cut
{
    internal static async Task RequireAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementPublicParentRf3NativeCut[] before, PartitionMovementPublicParentRf3NativeCut[] after)
    {
        await PartitionMovementActiveAdjunctRf3Assertions.RequireBusinessRowsUnchangedAsync(seed, before, after);
        var original = PartitionMovementReceiverIssueFailoverRf3NativeCut.Pending(after);
        await PartitionMovementReceiverIssueFailoverRf3NativeCut.RequireUnknownAsync(after, original, observed: true);
        await PartitionMovementReceiverIssueFailoverRf3ProofAssertions.RequireAsync(after, original, null);
        foreach (var control in after.Where(cut => cut.Header is not null))
        {
            await SqlRf3Protocol.EqualAsync(seed.OriginalPlacement, control.Header!.SourcePlacement);
            await Assert.That(control.Header.OriginalCapturePhaseCommandId).IsNotNull();
            await Assert.That(control.Header.InterruptedOriginalPhaseCommandId).IsNull();
        }
        if (before.Any(cut => cut.Pending is not null))
        {
            var previous = PartitionMovementReceiverIssueFailoverRf3NativeCut.Pending(before);
            await SqlRf3Protocol.EqualAsync(previous, original);
        }
    }
}
