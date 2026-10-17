using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestData;
using O = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestOperations;
using P = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestProjection;
using S = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStorePolicyPublicationData;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsStorePolicyPublicationFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        using var db = new TestDatabase(nativeReplicaAdmission: true);
        await O.InitializeAsync(db, privacy: true);
        var other = new ResourceDefinition(S.OtherCollection, ResourceKind.Collection, db.Partition.TransactionDomainId);
        await O.EqualAsync(other, AnalyticsExpectedResourceCreation.Configure(db, other, AnalyticsExpectedResourceCreation.Unrelated));
        AnalyticsEventData[] rows = [D.Event(db, D.First, D.Alpha, S.FirstValue, owned: true),
            D.Event(db, D.Second, D.Alpha, S.SecondValue, owned: true),
            D.Event(db, D.Third, D.Beta, S.ThirdValue, owned: true)];
        await O.CommitAsync(db, new UpsertAnalyticsEvents(D.Store, [.. rows],
            O.Revisions((D.First, S.Absent), (D.Second, S.Absent), (D.Third, S.Absent))),
            AnalyticsStoreProtocol.UpsertKind, D.Third, S.Revision, token);
        await AnalyticsStorePolicyPublicationOperations.ConfigureAsync(db, S.OriginalEpoch, [D.UseGrant]);
        var originalCut = AnalyticsStorePolicyPublicationCut.Capture(db);
        var original = db.Database.CaptureAnalyticsAggregate(D.Reader, D.Query(db), token);
        await originalCut.AssertUnchangedAsync(db);
        await P.RunAsync(original, S.Expected, token);
        await originalCut.AssertUnchangedAsync(db);
        await AnalyticsStorePolicyPublicationOperations.UnrelatedAsync(db, original, token);
        await AnalyticsStorePolicyPublicationOperations.ConfigureAsync(db, S.RevokedEpoch, []);
        var revokedCut = AnalyticsStorePolicyPublicationCut.Capture(db);
        await AnalyticsStorePolicyPublicationOperations.StaleAsync(db, original);
        await revokedCut.AssertUnchangedAsync(db);
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => db.Database.CaptureAnalyticsAggregate(D.Reader, D.Query(db), token));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(denied.Message).IsEqualTo(S.FieldDenied);
        await revokedCut.AssertUnchangedAsync(db);
        await AnalyticsStorePolicyPublicationOperations.ConfigureAsync(db, S.RestoredEpoch, [D.UseGrant, D.ReadGrant]);
        var restoredCut = AnalyticsStorePolicyPublicationCut.Capture(db);
        await P.AggregateAsync(db, D.Reader, S.Expected, token);
        await restoredCut.AssertUnchangedAsync(db);
        await O.RowsAsync(db, D.Reader, rows, [S.Revision, S.Revision, S.Revision], hideMetrics: false, token);
        await restoredCut.AssertUnchangedAsync(db);
        await AnalyticsStorePolicyPublicationOperations.StaleAsync(db, original);
        await restoredCut.AssertUnchangedAsync(db);
    }
}
