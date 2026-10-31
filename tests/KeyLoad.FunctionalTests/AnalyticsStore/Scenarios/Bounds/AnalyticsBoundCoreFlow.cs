using KeyLoad.Clustering;
using KeyLoad.Core.ResourceExecution;
using D = KeyLoad.FunctionalTests.AnalyticsStore.AnalyticsStoreTestData;
using O = KeyLoad.FunctionalTests.AnalyticsStore.AnalyticsStoreTestOperations;

namespace KeyLoad.FunctionalTests.AnalyticsStore;

internal static class AnalyticsBoundCoreFlow
{
    internal static Task HealthyAsync(CancellationToken token)
        => AnalyticsBoundCoreOwner.RunAsync(HealthyAsync, token);

    internal static Task HealthyAsync(TestDatabase fixture, RequestWorkOwner owner, CancellationToken token)
        => AnalyticsBoundCoreOwner.LeaseAsync(fixture, owner, async (lease, ledger, work) =>
            await RuntimeAnalyticsBounds.UnchangedAsync(fixture, async () =>
            {
                var expected = new AnalyticsEventsPage([new(D.First, D.From.AddMinutes(1),
                    System.Collections.Immutable.ImmutableDictionary<string, string>.Empty.Add(D.Kind, D.Alpha),
                    System.Collections.Immutable.ImmutableDictionary<string, double>.Empty.Add(D.Value, 2), null, [], 1)], false, null);
                await O.EqualAsync(expected, fixture.Database.ReadAnalyticsEventsBound(D.Root, D.Read(fixture), work, lease));
                var lifetime = fixture.Store.Read(view => (ResourceLifetimeCatalog.Read(view, fixture.Partition.TenantId,
                    fixture.Partition.DatabaseId, D.Store, fixture.Database.Resource(view, fixture.Partition, D.Store))).Value)
                    ?? throw new InvalidOperationException("The actual analytics lifetime is absent.");
                await Assert.That(lease.Dependencies.SetEquals([lifetime])).IsTrue();
                await Assert.That(ledger.RetainedEntries).IsEqualTo(1);
                var snapshot = fixture.Database.CaptureAnalyticsAggregateBound(D.Root, D.Query(fixture), work, lease);
                await Assert.That(ReferenceEquals(snapshot.Work, work)).IsTrue();
                await AnalyticsStoreTestProjection.RunAsync(snapshot, new([D.Row(D.Alpha, 1, 2, 2, 2, 2)], 1), token);
                fixture.Database.ValidateAnalyticsAggregatePublication(snapshot);
                await Assert.That(ledger.RetainedEntries).IsEqualTo(1);
            }), token);
}
