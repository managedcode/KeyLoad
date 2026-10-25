namespace KeyLoad.FunctionalTests.Authorization;

internal static class SafeProjectorOperationColdFeed
{
    private const long FinalSequence = 4;
    private const long FirstSequence = 1;
    private const string Purpose = "document-change-feed";
    private const long SchemaVersion = 1;
    private const long VisibilityEpoch = 0;

    internal static async Task ContinueAsync(SafeProjectorOperationDatabase owner, string originalCursor)
    {
        await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
        {
            var cut = owner.Store.Position;
            var before = owner.Database.EvaluationClock.GetUtcNow();
            var page = owner.Database.ReadChangeFeed(SafeProjectorOperationProtocol.Reader,
                new(owner.Source.Partition, SafeProjectorOperationProtocol.Collection, Cursor: originalCursor), owner.CallerToken);
            var after = owner.Database.EvaluationClock.GetUtcNow();
            var claims = owner.Database.Verify<ChangeFeedClaims>(page.Cursor);
            var lifetime = UnitExecutionOptions.ChangeFeedExecution().Value.CursorLifetime;
            await Assert.That(claims.ExpiresAt >= before.Add(lifetime) && claims.ExpiresAt <= after.Add(lifetime)).IsTrue();
            var expected = new ChangeFeedClaims(Purpose, owner.Store.Identity.Incarnation, owner.Source.Partition,
                SafeProjectorOperationProtocol.Collection, SafeProjectorOperationProtocol.Reader,
                SafeProjectorOperationProtocol.FinalMaskedEpoch, SchemaVersion, VisibilityEpoch, FinalSequence, claims.ExpiresAt);
            await SafeProjectorOperationSeed.EqualAsync(claims, expected);
            await SafeProjectorOperationSeed.EqualAsync(page, new ChangeFeedPage([], owner.Database.Sign(expected),
                FinalSequence, FinalSequence, FirstSequence, false, cut));
        });
    }
}
