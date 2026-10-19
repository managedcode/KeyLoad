using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreTestData;
using O = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreTestOperations;
using P = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreTestProjection;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsStorePrivacyFlow
{
    private const string UpsertKind = "upsertAnalyticsEvents";
    private const string FieldDenied = "The query requires a protected field-use grant.";
    internal static async Task RunAsync(CancellationToken token)
    {
        using var db = new TestDatabase();
        await O.InitializeAsync(db, true);
        AnalyticsEventData[] rows = [D.Event(db, D.First, D.Alpha, 2, true), D.Event(db, D.Second, D.Alpha, 4, true), D.Event(db, D.Third, D.Beta, 8, true)];
        var hidden = D.Event(db, D.Hidden, D.Beta, 999) with
        {
            RowAccess = new(OwnerId: D.Other)
        };
        await O.CommitAsync(db, new UpsertAnalyticsEvents(D.Store, [.. rows, hidden],
            O.Revisions((D.First, 0), (D.Second, 0), (D.Third, 0), (D.Hidden, 0))), UpsertKind, D.Hidden, 1, token);
        await ConfigureAsync(db, 1, []);
        await O.RowsAsync(db, D.Reader, rows, [1, 1, 1], true, token);
        var before = db.Database.GetOutboxStatus(D.Root, db.Partition);
        var denied = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => Task.FromResult(db.Database.CaptureAnalyticsAggregate(D.Reader, D.Query(db), token)));
        ArgumentNullException.ThrowIfNull(denied);
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(denied.Message).IsEqualTo(FieldDenied);
        await O.EqualAsync(before, db.Database.GetOutboxStatus(D.Root, db.Partition));
        await ConfigureAsync(db, 2, [D.UseGrant]);
        await P.AggregateAsync(db, D.Reader, new([D.Row(D.Alpha, 2, 6, 3, 2, 4), D.Row(D.Beta, 1, 8, 8, 8, 8)], 3), token);
        await O.RowsAsync(db, D.Reader, rows, [1, 1, 1], true, token);
        var oldPolicy = db.Database.CaptureAnalyticsAggregate(D.Reader, D.Query(db), token);
        await ConfigureAsync(db, 3, [D.ReadGrant]);
        await P.RunAsync(oldPolicy, new([D.Row(D.Alpha, 2, 6, 3, 2, 4), D.Row(D.Beta, 1, 8, 8, 8, 8)], 3), token);
        var stale = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
        {
            db.Database.ValidateAnalyticsAggregatePublication(oldPolicy);
            return Task.CompletedTask;
        });
        ArgumentNullException.ThrowIfNull(stale);
        await Assert.That(stale.Code).IsEqualTo(ErrorCode.TokenInvalidated);
        await O.RowsAsync(db, D.Reader, rows, [1, 1, 1], false, token);
        await ConfigureAsync(db, 4, [D.UseGrant, D.ReadGrant]);
        await P.AggregateAsync(db, D.Reader, new([D.Row(D.Alpha, 2, 6, 3, 2, 4), D.Row(D.Beta, 1, 8, 8, 8, 8)], 3), token);
        await O.RowsAsync(db, D.Reader, rows, [1, 1, 1], false, token);
        await O.EqualAsync(before, db.Database.GetOutboxStatus(D.Root, db.Partition));
    }

    private static async Task ConfigureAsync(TestDatabase db, long epoch, System.Collections.Immutable.ImmutableArray<string> grants)
    {
        var principal = D.Principal(db, epoch, grants);
        await O.EqualAsync(principal, db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>());
    }
}
