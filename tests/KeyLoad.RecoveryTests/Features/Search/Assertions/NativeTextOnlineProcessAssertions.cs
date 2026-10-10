using KeyLoad.CrashHost.Features.Search;

namespace KeyLoad.RecoveryTests.Features.Search;

internal static class NativeTextOnlineProcessAssertions
{
    private const int TrackedBilingualRecords = 2;
    internal static async Task VerifyAsync(string root, string mode, CancellationToken token)
    {
        if (mode is NativeTextOnlineCrashScenario.Prepare or NativeTextOnlineCrashScenario.Fault)
        { return; }
        var original = await NativeTextIncrementalEvidenceFiles.ReadAsync<NativeTextOnlineCrashOriginal>(root,
            NativeTextOnlineCrashScenario.OriginalFile, token);
        var receipt = await NativeTextIncrementalEvidenceFiles.ReadAsync<OnlineTextIndexMaintenanceResult>(root,
            NativeTextOnlineCrashScenario.ReceiptFile, token);
        var healthy = await NativeTextIncrementalEvidenceFiles.ReadAsync<OnlineTextIndexMaintenanceRequest>(root,
            NativeTextOnlineCrashScenario.HealthyRequestFile, token);
        var healthyReceipt = await NativeTextIncrementalEvidenceFiles.ReadAsync<OnlineTextIndexMaintenanceResult>(root,
            NativeTextOnlineCrashScenario.HealthyReceiptFile, token);
        await Assert.That(receipt.CommandId).IsEqualTo(original.Request.CommandId);
        await Assert.That(receipt.Consumer).IsEqualTo(original.Request.Consumer);
        await Assert.That(receipt.TrackedRecords).IsEqualTo(TrackedBilingualRecords);
        await Assert.That(receipt.Checkpoint.Receipt.Mutations).IsEmpty();
        await Assert.That(receipt.PublishedCut.Incarnation).IsEqualTo(original.Request.Placement.Incarnation);
        await Assert.That(receipt.PublishedCut.ReadGeneration).IsEqualTo(receipt.BaseCut.ReadGeneration);
        await Assert.That(healthy.CommandId).IsNotEqualTo(original.Request.CommandId);
        await Assert.That(healthyReceipt.CommandId).IsEqualTo(healthy.CommandId);
        await Assert.That(healthyReceipt.PublishedCut.NodeId).IsEqualTo(receipt.PublishedCut.NodeId);
        await Assert.That(healthyReceipt.PublishedCut.Incarnation).IsEqualTo(receipt.PublishedCut.Incarnation);
        await Assert.That(healthyReceipt.PublishedCut.AppliedPosition).IsGreaterThan(receipt.PublishedCut.AppliedPosition);
        await Assert.That(healthyReceipt.PublishedCut.ReadGeneration).IsGreaterThanOrEqualTo(receipt.PublishedCut.ReadGeneration);
        await Assert.That(healthyReceipt.TrackedRecords).IsEqualTo(TrackedBilingualRecords);
        await Assert.That(healthyReceipt.Checkpoint.Receipt.Mutations).IsEmpty();
    }
}
