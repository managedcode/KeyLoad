using System.Collections.Immutable;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class SafeProjectorOperationChanges
{
    private const string Purpose = "document-change-feed";
    private const long SchemaVersion = 1;
    private const long VisibilityEpoch = 0;
    private const long FirstAvailable = 1;
    private const long FirstSequence = 1;
    private const long SecondSequence = 2;
    private const long PublicPatchSequence = 3;
    private const long HealthyPatchSequence = 4;

    internal static async Task<ChangeFeedPage> CurrentAsync(SafeProjectorOperationDatabase owner,
        SafeProjectorOperationState state, long policyEpoch, bool raw, CancellationToken token,
        SafeProjectorOperationCommand? patch = null, SafeProjectorOperationCommand? healthy = null)
    {
        ChangeFeedPage? page = null;
        await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
        {
            var cut = owner.Store.Position;
            var before = owner.Database.EvaluationClock.GetUtcNow();
            var actual = owner.Database.ReadChangeFeed(SafeProjectorOperationProtocol.Reader,
                new(owner.Source.Partition, SafeProjectorOperationProtocol.Collection), token);
            var after = owner.Database.EvaluationClock.GetUtcNow();
            var expected = Changes(owner, state, raw, patch, healthy);
            var tail = healthy is not null ? HealthyPatchSequence : patch is not null ? PublicPatchSequence : SecondSequence;
            var claims = owner.Database.Verify<ChangeFeedClaims>(actual.Cursor);
            var lifetime = UnitExecutionOptions.ChangeFeedExecution().Value.CursorLifetime;
            await Assert.That(claims.ExpiresAt >= before.Add(lifetime) && claims.ExpiresAt <= after.Add(lifetime)).IsTrue();
            var expectedCursor = owner.Database.Sign(new ChangeFeedClaims(Purpose, owner.Store.Identity.Incarnation,
                owner.Source.Partition, SafeProjectorOperationProtocol.Collection, SafeProjectorOperationProtocol.Reader,
                policyEpoch, SchemaVersion, VisibilityEpoch, tail, claims.ExpiresAt));
            await SafeProjectorOperationSeed.EqualAsync(claims, owner.Database.Verify<ChangeFeedClaims>(expectedCursor));
            await SafeProjectorOperationSeed.EqualAsync(actual, new ChangeFeedPage(expected, expectedCursor, tail,
                tail, FirstAvailable, false, cut));
            page = actual;
        });
        return page ?? throw new InvalidOperationException(MissingPage);
    }

    private const string MissingPage = "The actual complete safe projector change page is absent.";

    private static ImmutableArray<DocumentChange> Changes(SafeProjectorOperationDatabase owner,
        SafeProjectorOperationState state, bool raw, SafeProjectorOperationCommand? patch,
        SafeProjectorOperationCommand? healthy)
    {
        var source = owner.Source.ExpectedReference(new(owner.Source.Partition, SafeProjectorOperationProtocol.Collection, SafeProjectorOperationProtocol.First));
        var second = owner.Source.ExpectedReference(new(owner.Source.Partition, SafeProjectorOperationProtocol.Collection, SafeProjectorOperationProtocol.Second));
        var initial = Document(source, SafeProjectorOperationProtocol.InitialRevision,
            raw ? SafeProjectorOperationProtocol.InitialJson : SafeProjectorOperationProtocol.InitialSafe, raw);
        var rows = ImmutableArray.CreateBuilder<DocumentChange>();
        rows.Add(new(FirstSequence, state.Seed.ExpectedToken, new DateTimeOffset(state.Seed.Operation.Timestamp), source,
            SafeProjectorOperationProtocol.InitialRevision, false, null, initial));
        rows.Add(new(SecondSequence, state.Seed.ExpectedToken, new DateTimeOffset(state.Seed.Operation.Timestamp), second,
            SafeProjectorOperationProtocol.InitialRevision, false, null,
            Document(second, SafeProjectorOperationProtocol.InitialRevision, SafeProjectorOperationProtocol.SecondJson, raw)));
        var updated = Document(source, SafeProjectorOperationProtocol.UpdatedRevision,
            raw ? SafeProjectorOperationProtocol.UpdatedJson : SafeProjectorOperationProtocol.UpdatedSafe, raw);
        if (patch is not null)
        {
            rows.Add(new(PublicPatchSequence, patch.ExpectedToken, new DateTimeOffset(patch.Operation.Timestamp), source,
                SafeProjectorOperationProtocol.UpdatedRevision, false, initial, updated));
        }
        if (healthy is not null)
        {
            rows.Add(new(HealthyPatchSequence, healthy.ExpectedToken, new DateTimeOffset(healthy.Operation.Timestamp), source,
                SafeProjectorOperationProtocol.HealthyRevision, false, updated,
                Document(source, SafeProjectorOperationProtocol.HealthyRevision,
                    raw ? SafeProjectorOperationProtocol.HealthyJson : SafeProjectorOperationProtocol.HealthySafe, raw)));
        }
        return rows.ToImmutable();
    }

    private static DocumentResult Document(EntityRef reference, long revision, string json, bool raw)
        => new(reference, revision, json, !raw, raw ? [] : SafeProjectorOperationProtocol.RedactedPaths);
}
