namespace KeyLoad.ProcessRecoveryTests.Search;

internal static class TextOnlineProcessAssertions
{
    private const int TrackedBilingualRecords = 2;
    internal static async Task VerifyAsync(string root, string mode, CancellationToken token)
    {
        if (mode is TextOnlineCrashScenario.Prepare or TextOnlineCrashScenario.Fault or TextOnlineCrashScenario.Corrupt)
        {
            return;
        }
        var original = await TextIncrementalEvidenceFiles.ReadAsync<TextOnlineCrashOriginal>(root,
            TextOnlineCrashScenario.OriginalFile, token);
        var receipt = await TextIncrementalEvidenceFiles.ReadAsync<OnlineTextIndexMaintenanceResult>(root,
            TextOnlineCrashScenario.ReceiptFile, token);
        var healthy = await TextIncrementalEvidenceFiles.ReadAsync<OnlineTextIndexMaintenanceRequest>(root,
            TextOnlineCrashScenario.HealthyRequestFile, token);
        var healthyReceipt = await TextIncrementalEvidenceFiles.ReadAsync<OnlineTextIndexMaintenanceResult>(root,
            TextOnlineCrashScenario.HealthyReceiptFile, token);
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
