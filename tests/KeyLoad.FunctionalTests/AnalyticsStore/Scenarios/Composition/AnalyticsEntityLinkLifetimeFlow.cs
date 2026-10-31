using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using A = KeyLoad.FunctionalTests.AnalyticsStore.AnalyticsEntityLinkLifetimeAssertions;
using D = KeyLoad.FunctionalTests.AnalyticsStore.AnalyticsStoreTestData;
using F = KeyLoad.FunctionalTests.AnalyticsStore.AnalyticsEntityLinkLifetimeFixture;

namespace KeyLoad.FunctionalTests.AnalyticsStore;

internal static class AnalyticsEntityLinkLifetimeFlow
{
    private const string ChangedLifetime = "The entity reference belongs to another resource lifetime.";

    internal static Task IncomingAsync(CancellationToken token) => F.RunAsync(async fixture =>
    {
        var original = await CommitAsync(fixture, fixture.Plain, 0, token);
        await A.UnchangedAsync(fixture, async () =>
        {
            await A.RowsAsync(fixture, original, 1, token);
            var retained = fixture.Owner.ReadOutcome(original).PreparedCommand;
            await Assert.That(OrleansSerialization.Serialize(retained.Value!.Input, ValidationProfile.PublicInputElements).AsSpan().SequenceEqual(OrleansSerialization.Serialize(original.Value!.Input, ValidationProfile.PublicInputElements).AsSpan())).IsTrue();
            var payload = retained.Value!;
            var input = ((CommandRequest)payload.Input);
            await A.EqualAsync(fixture.Plain, ((UpsertAnalyticsEvents)input.Mutations.Single()).Events.Single().Links.Single());
        });
        var bound = await CommitAsync(fixture, fixture.Bound, 1, token);
        await A.UnchangedAsync(fixture, () => A.RowsAsync(fixture, bound, 2, token));
        await ForeignRefusalAsync(fixture, token);
        await A.UnchangedAsync(fixture, () => A.RowsAsync(fixture, bound, 2, token));
        var healthy = await CommitAsync(fixture, fixture.Bound, 2, token);
        await A.UnchangedAsync(fixture, () => A.RowsAsync(fixture, healthy, 3, token));
    }, token);

    internal static Task StoredCorruptionAsync(bool foreign, CancellationToken token) => F.RunAsync(async fixture =>
    {
        var original = await CommitAsync(fixture, fixture.Bound, 0, token);
        await fixture.CorruptStoredLinkAsync(foreign, () => A.UnchangedAsync(fixture, async () =>
        {
            AnalyticsEventsPage? published = null;
            var refusal = Assert.ThrowsExactly<KeyLoadException>(() => published = fixture.Owner.Database.ReadAnalyticsEvents(
                D.Root, D.Read(fixture.Owner.Source), token));
            await Assert.That(refusal.Code).IsEqualTo(foreign ? ErrorCode.OwnershipLost : ErrorCode.FormatUnsupported);
            await Assert.That(published).IsNull();
        }));
        await A.UnchangedAsync(fixture, () => A.RowsAsync(fixture, original, 1, token));
        var healthy = await CommitAsync(fixture, fixture.Plain, 1, token);
        await A.UnchangedAsync(fixture, () => A.RowsAsync(fixture, healthy, 2, token));
    }, token);

    private static Command<DatabaseCommand> Issue(F fixture, EntityRef link, long revision, CancellationToken token)
    {
        var owner = fixture.Owner;
        var id = Guid.NewGuid();
        var value = D.Event(owner.Source, D.First, D.Alpha, 2) with
        {
            Links = [link]
        };
        var request = new CommandRequest(id, owner.Source.Partition,
            [new UpsertAnalyticsEvents(D.Store, [value], AnalyticsStoreTestOperations.Revisions((D.First, revision)))]);
        var work = new ReadExecutionBudget(owner.Database.EvaluationClock, token);
        return owner.Database.CreateCommand(OperationKind.Batch, id, D.Root, owner.Database.EvaluationClock.GetUtcNow(),
            request, work);
    }

    private static async Task<Command<DatabaseCommand>> CommitAsync(F fixture, EntityRef link, long revision, CancellationToken token)
    {
        var original = Issue(fixture, link, revision, token);
        var expected = new CommitReceipt(original.CommandId, fixture.Owner.NextToken(), [new(AnalyticsStoreProtocol.UpsertKind, D.Store, D.First, revision + 1)], DurabilityProfile.ProcessDurable, 1);
        await A.ReceiptAsync(expected, fixture.Owner.Submit(original).Get<CommitReceipt>());
        return original;
    }

    private static async Task ForeignRefusalAsync(F fixture, CancellationToken token)
    {
        var owner = fixture.Owner;
        var business = A.BusinessImage(fixture);
        var original = Issue(fixture, fixture.Foreign, 2, token);
        var index = owner.ReadCanonicalCut().AppliedPosition + 1;
        var position = owner.Store.Position;
        var result = owner.Submit(original);
        await A.EqualAsync(Result<object>.Fail(Errors.Problem(ErrorCode.OwnershipLost, ChangedLifetime)), result);
        await Assert.That(result.Value).IsNull();
        await Assert.That(A.BusinessImage(fixture).SequenceEqual(business, StringComparer.Ordinal)).IsTrue();
        await Assert.That(owner.ReadCanonicalCut().AppliedPosition).IsEqualTo(index);
        await Assert.That(owner.Database.LastApplied).IsEqualTo(index);
        await Assert.That(owner.Store.Position).IsEqualTo(position + 1);
        // Failed command metadata is a legitimate execution commit, not a business mutation.
        var key = OutcomeStoreOracle.PartitionKey(owner.Source.Partition, D.Root, original.CommandId);
        var stored = owner.Store.Read(view => view.GetRecord<StoredOutcome>(key))!;
        await A.EqualAsync(result, stored.Result);
        await A.UnchangedAsync(fixture, () => A.EqualAsync(result, owner.Submit(original)));
    }
}
