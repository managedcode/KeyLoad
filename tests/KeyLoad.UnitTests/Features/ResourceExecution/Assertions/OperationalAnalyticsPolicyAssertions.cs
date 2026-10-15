namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class OperationalAnalyticsPolicyAssertions
{
    private const string DerivedDirectory = "derived-operational-analytics";

    internal static OperationalAnalyticsPolicyCut Capture(OperationalAnalyticsNativeOwner owner)
    {
        var database = owner.Canonical;
        return new(OperationalAnalyticsNativeSeed.Rows(database), database.Store.Position, database.Store.Identity with
        {
            SigningKey = database.Store.Identity.SigningKey.ToArray()
        }, database.ReadJournalCut());
    }

    internal static async Task UnchangedAsync(OperationalAnalyticsNativeOwner owner, OperationalAnalyticsPolicyCut before)
    {
        await Assert.That(Directory.Exists(Path.Combine(owner.NodeDirectory, DerivedDirectory))).IsFalse();
        var database = owner.Canonical;
        await Assert.That(OperationalAnalyticsNativeSeed.Rows(database)
            .SequenceEqual(before.Rows, StringComparer.Ordinal)).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(before.Position);
        await Assert.That(database.ReadJournalCut()).IsEqualTo(before.Journal);
        var identity = database.Store.Identity;
        await Assert.That(identity.FormatVersion).IsEqualTo(before.Identity.FormatVersion);
        await Assert.That(identity.KeyCodecVersion).IsEqualTo(before.Identity.KeyCodecVersion);
        await Assert.That(identity.NodeId).IsEqualTo(before.Identity.NodeId);
        await Assert.That(identity.Incarnation).IsEqualTo(before.Identity.Incarnation);
        await Assert.That(identity.SigningKey.Span.SequenceEqual(before.Identity.SigningKey.Span)).IsTrue();
        await Assert.That(identity.Durability).IsEqualTo(before.Identity.Durability);
        await Assert.That(identity.DispatchPaused).IsEqualTo(before.Identity.DispatchPaused);
        await Assert.That(identity.ReadGeneration).IsEqualTo(before.Identity.ReadGeneration);
        await Assert.That(identity.MinimumReaderContract).IsEqualTo(before.Identity.MinimumReaderContract);
    }
}
