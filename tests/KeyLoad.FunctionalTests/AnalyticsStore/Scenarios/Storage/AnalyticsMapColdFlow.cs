using KeyLoad.FunctionalTests.Authorization;
using D = KeyLoad.FunctionalTests.AnalyticsStore.AnalyticsStoreTestData;
using O = KeyLoad.FunctionalTests.AnalyticsStore.AnalyticsStoreTestOperations;

namespace KeyLoad.FunctionalTests.AnalyticsStore;

internal static class AnalyticsMapColdFlow
{
    internal static Task RunAsync(CancellationToken token)
        => SafeProjectorOperationDatabase.RunAsync(async owner =>
        {
            await O.InitializeAsync(owner.Source, false);
            AnalyticsEventData[] events = [D.Event(owner.Source, D.First, D.Alpha, 2) with { Links = [] },
                D.Event(owner.Source, D.Second, D.Alpha, 4) with { Links = [] },
                D.Event(owner.Source, D.Third, D.Beta, 8) with { Links = [] }];
            var id = Guid.NewGuid();
            var request = new CommandRequest(id, owner.Source.Partition,
                [new UpsertAnalyticsEvents(D.Store, [.. events], O.Revisions((D.First, 0), (D.Second, 0), (D.Third, 0)))]);
            // Submit owns the actual public JSON -> NormalizePayload -> execution ordered journal/apply path.
            var expected = new CommitReceipt(id, owner.NextToken(), [new(AnalyticsStoreProtocol.UpsertKind, D.Store, D.Third, 1)], DurabilityProfile.ProcessDurable, 1);
            await O.EqualAsync(expected, owner.Source.Submit(OperationKind.Batch, request, id: id).Get<CommitReceipt>());
            var original = owner.Source.ReadStoredOutcome(id).PreparedCommand
                ?? throw new InvalidOperationException("The original execution analytics command was not retained.");
            var decoded = ((CommandRequest)(original).Value!.Input);
            await O.EqualAsync(request, decoded);
            await Assert.That(decoded.Mutations.Single()).IsTypeOf<UpsertAnalyticsEvents>();
            await AnalyticsMapColdReads.VerifyAsync(owner, events, token);
            await AnalyticsMapColdReads.RefusalsAsync(owner, events, token);
            var outbox = owner.Database.GetOutboxStatus(D.Root, owner.Source.Partition);
            for (var cycle = 0; cycle < 2; cycle++)
            {
                var image = TeamRowPolicyAssertions.Image(owner.Store);
                var identity = OrleansSerialization.Serialize(owner.Store.Identity);
                var position = owner.Store.Position;
                var journal = owner.ReadCanonicalCut();
                var applied = owner.Database.LastApplied;
                await owner.ReopenAsync();
                await Assert.That(TeamRowPolicyAssertions.Image(owner.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
                await Assert.That(OrleansSerialization.Serialize(owner.Store.Identity).SequenceEqual(identity)).IsTrue();
                await Assert.That(owner.Store.Position).IsEqualTo(position);
                await Assert.That(owner.ReadCanonicalCut()).IsEqualTo(journal);
                await Assert.That(owner.Database.LastApplied).IsEqualTo(applied);
                await AnalyticsMapColdReads.VerifyAsync(owner, events, token);
                await AnalyticsMapColdReads.UnchangedAsync(owner, async () =>
                {
                    await O.EqualAsync(expected, owner.Submit(original).Get<CommitReceipt>());
                    await O.EqualAsync(outbox, owner.Database.GetOutboxStatus(D.Root, owner.Source.Partition));
                    await AnalyticsMapColdReads.VerifyAsync(owner, events, token);
                });
            }
        }, token);
}
