using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestData;
using O = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestOperations;
using P = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestProjection;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsStoreNativeMutationFlow
{
    private const string UpsertKind = "upsertAnalyticsEvents";
    private const string DeleteKind = "deleteAnalyticsEvents";
    internal static async Task RunAsync(CancellationToken token)
    {
        using var db = new TestDatabase(nativeReplicaAdmission: true);
        await O.InitializeAsync(db, false);
        AnalyticsEventData[] rows = [D.Event(db, D.First, D.Alpha, 2), D.Event(db, D.Second, D.Alpha, 4), D.Event(db, D.Third, D.Beta, 8)];
        var seed = await O.CommitAsync(db, new UpsertAnalyticsEvents(D.Store, [.. rows], O.Revisions((D.First, 0), (D.Second, 0), (D.Third, 0))), UpsertKind, D.Third, 1, token);
        await O.RowsAsync(db, D.Root, rows, [1, 1, 1], false, token);
        await P.AggregateAsync(db, D.Root, new([D.Row(D.Alpha, 2, 6, 3, 2, 4), D.Row(D.Beta, 1, 8, 8, 8, 8)], 3), token);
        rows[0] = D.Event(db, D.First, D.Alpha, 10);
        await O.CommitAsync(db, new UpsertAnalyticsEvents(D.Store, [rows[0]], O.Revisions((D.First, 1))), UpsertKind, D.First, 2, token);
        await O.RowsAsync(db, D.Root, rows, [2, 1, 1], false, token);
        await P.AggregateAsync(db, D.Root, new([D.Row(D.Alpha, 2, 14, 7, 4, 10), D.Row(D.Beta, 1, 8, 8, 8, 8)], 3), token);
        await O.CommitAsync(db, new DeleteAnalyticsEvents(D.Store, O.Revisions((D.Second, 1))), DeleteKind, D.Second, 3, token);
        await O.RowsAsync(db, D.Root, [rows[0], rows[2]], [2, 1], false, token);
        await P.AggregateAsync(db, D.Root, new([D.Row(D.Alpha, 1, 10, 10, 10, 10), D.Row(D.Beta, 1, 8, 8, 8, 8)], 2), token);
        await O.CommitAsync(db, new DeleteAnalyticsEvents(D.Store, O.Revisions((D.First, 2), (D.Third, 1))), DeleteKind, D.Third, 4, token);
        await O.RowsAsync(db, D.Root, [], [], false, token);
        await P.AggregateAsync(db, D.Root, new([], 0), token);
        await O.RefusedAsync(db, new UpsertAnalyticsEvents(D.Store, [rows[0]], O.Revisions((D.First, 0))), token);
        var replay = db.SubmitIssued(seed.Operation).Get<CommitReceipt>();
        await O.EqualAsync(seed.Receipt, replay);
        await Assert.That(replay.Mutations.Single().CompositionReferences.IsEmpty).IsTrue();
        await O.RowsAsync(db, D.Root, [], [], false, token);
        await P.AggregateAsync(db, D.Root, new([], 0), token);
    }
}
