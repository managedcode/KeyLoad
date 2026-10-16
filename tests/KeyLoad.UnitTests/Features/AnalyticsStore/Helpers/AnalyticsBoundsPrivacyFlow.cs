using System.Collections.Immutable;
using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestData;
using N = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsBoundsNative;
using O = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreNativeTestOperations;
using P = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsBoundsData;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsBoundsPrivacyFlow
{
    private const string DimensionPath = "/dimensions/kind";
    private const string UseDenied = "The query requires a protected field-use grant.";
    internal static async Task RunAsync(CancellationToken token)
    {
        using var db = new TestDatabase(nativeReplicaAdmission: true);
        AnalyticsEventData[] events = [D.Event(db, D.First, P.Padded, 2, true), D.Event(db, D.Second, P.Plain, 4, true)];
        await AnalyticsBoundsOwner.SeedAsync(db, events);
        var definition = new ResourceDefinition(D.Store, ResourceKind.AnalyticsStore, db.Partition.TransactionDomainId)
        {
            AnalyticsSchema = new([D.Kind], [D.Value]),
            SchemaVersion = 2,
            FieldPolicies = [new(DimensionPath, D.Classification, D.ReadGrant, D.UseGrant, D.WriteGrant)]
        };
        await O.EqualAsync(definition, db.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(db.Partition.TenantId, db.Partition.DatabaseId, definition)
            {
                ExpectedSchemaVersion = 1
            }).Get<ResourceDefinition>());
        await PolicyAsync(db, 1, []);
        var query = P.Query(db);
        await N.UnchangedAsync(db, async () =>
        {
            var denied = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => Task.FromResult(
                db.Database.CaptureAnalyticsAggregate(D.Reader, query, token)));
            ArgumentNullException.ThrowIfNull(denied);
            await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(denied.Message).IsEqualTo(UseDenied);
            await RawAsync(db, events, token);
        });
        await PolicyAsync(db, 2, [D.UseGrant]);
        var expected = new AnalyticsAggregateResult([P.Row(P.Padded, 1, 2), P.Row(P.Plain, 1, 4)], 2);
        await N.UnchangedAsync(db, async () =>
        {
            var snapshot = db.Database.CaptureAnalyticsAggregate(D.Reader, query, token);
            await AnalyticsStoreNativeTestProjection.RunAsync(snapshot, expected, token);
            db.Database.ValidateAnalyticsAggregatePublication(snapshot);
            await RawAsync(db, events, token);
        });
        var previous = db.Database.CaptureAnalyticsAggregate(D.Reader, query, token);
        await PolicyAsync(db, 3, [D.ReadGrant]);
        await N.UnchangedAsync(db, async () =>
        {
            await AnalyticsStoreNativeTestProjection.RunAsync(previous, expected, token);
            var denied = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
                {
                    db.Database.ValidateAnalyticsAggregatePublication(previous);
                    return Task.CompletedTask;
                });
            ArgumentNullException.ThrowIfNull(denied);
            await Assert.That(denied.Code).IsEqualTo(ErrorCode.TokenInvalidated);
        });
        await PolicyAsync(db, 4, [D.ReadGrant, D.UseGrant]);
        await N.UnchangedAsync(db, async () =>
        {
            var healthy = db.Database.CaptureAnalyticsAggregate(D.Reader, query, token);
            await AnalyticsStoreNativeTestProjection.RunAsync(healthy, expected, token);
            db.Database.ValidateAnalyticsAggregatePublication(healthy);
        });
    }

    private static async Task PolicyAsync(TestDatabase db, long epoch, ImmutableArray<string> grants)
    {
        var principal = D.Principal(db, epoch, grants);
        await O.EqualAsync(principal, db.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>());
    }

    private static Task RawAsync(TestDatabase db, AnalyticsEventData[] events, CancellationToken token)
        => O.EqualAsync(new AnalyticsEventsPage([.. events.Select(row => new AnalyticsEventRecord(row.Id,
            row.Timestamp, ImmutableDictionary<string, string>.Empty, row.Metrics, row.RowAccess, row.Links, 1))], false, null),
            db.Database.ReadAnalyticsEvents(D.Reader, D.Read(db), token));
}
