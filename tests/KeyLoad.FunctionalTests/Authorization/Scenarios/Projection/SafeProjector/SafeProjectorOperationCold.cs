namespace KeyLoad.FunctionalTests.Authorization;

internal static class SafeProjectorOperationCold
{
    private const int ColdOwners = 2;
    private const long HealthyDocumentEpoch = 3;

    internal static async Task RunAsync(SafeProjectorOperationDatabase owner, SafeProjectorOperationHistory history)
    {
        await SafeProjectorOperationReplay.AllAsync(owner, history.Commands, SafeProjectorOperationProtocol.FinalMaskedEpoch);
        var queryCursor = await SafeProjectorOperationCursor.CaptureAsync(owner,
            SafeProjectorOperationProtocol.FinalMaskedEpoch, HealthyDocumentEpoch,
            SafeProjectorOperationProtocol.HealthySafe, SafeProjectorOperationProtocol.HealthyRevision, false, owner.CallerToken);
        var feed = await SafeProjectorOperationChanges.CurrentAsync(owner, history.State,
            SafeProjectorOperationProtocol.FinalMaskedEpoch, false, owner.CallerToken, history.PublicPatch, history.HealthyPatch);
        for (var cold = 0; cold < ColdOwners; cold++)
        {
            var image = TeamRowPolicyAssertions.Image(owner.Store);
            var position = owner.Store.Position;
            var journal = owner.ReadCanonicalCut();
            var applied = owner.Database.LastApplied;
            await owner.ReopenAsync();
            await Assert.That(TeamRowPolicyAssertions.Image(owner.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
            await Assert.That(owner.Store.Position).IsEqualTo(position);
            await Assert.That(owner.ReadCanonicalCut()).IsEqualTo(journal);
            await Assert.That(owner.Database.LastApplied).IsEqualTo(applied);
            await history.CurrentAsync(owner, SafeProjectorOperationProtocol.FinalMaskedEpoch, [SafeProjectorOperationProtocol.WriteGrant]);
            await SafeProjectorOperationReplay.AllAsync(owner, history.Commands, SafeProjectorOperationProtocol.FinalMaskedEpoch);
            await SafeProjectorOperationContinuation.FinalAsync(owner, history);
            await SafeProjectorOperationCursor.CaptureAsync(owner, SafeProjectorOperationProtocol.FinalMaskedEpoch,
                HealthyDocumentEpoch, SafeProjectorOperationProtocol.HealthySafe,
                SafeProjectorOperationProtocol.HealthyRevision, false, owner.CallerToken);
            await SameCursorAsync(owner, queryCursor, feed.Cursor);
        }
    }

    private static async Task SameCursorAsync(SafeProjectorOperationDatabase owner, string queryCursor, string feedCursor)
    {
        await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
        {
            var query = new KeyLoad.Query.QueryEngine(owner.Database, UnitExecutionOptions.QueryExecution());
            var page = query.Execute(SafeProjectorOperationProtocol.Reader,
                new(owner.Source.Partition, SafeProjectorOperationRequests.PageSql, AllowFullScan: true, Cursor: queryCursor),
                cancellationToken: owner.CallerToken);
            await SafeProjectorOperationSeed.EqualAsync(page, new QueryPage(
                [new(SafeProjectorOperationProtocol.Second, SafeProjectorOperationProtocol.InitialRevision,
                    SafeProjectorOperationProtocol.SecondJson, true, SafeProjectorOperationProtocol.RedactedPaths)],
                null, owner.Store.Position, FullScan));
        });
        await SafeProjectorOperationColdFeed.ContinueAsync(owner, feedCursor);
    }

    private const string FullScan = "bounded-full-scan";
}
