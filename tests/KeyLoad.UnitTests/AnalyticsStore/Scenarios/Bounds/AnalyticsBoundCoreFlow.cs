using KeyLoad.Clustering;
using KeyLoad.Core.Features.ResourceExecution;
using D = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreTestData;
using O = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsStoreTestOperations;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

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
                var lifetime = fixture.Store.Read(view => ResourceLifetimeCatalog.Read(view, fixture.Partition.TenantId,
                    fixture.Partition.DatabaseId, D.Store, fixture.Database.Resource(view, fixture.Partition, D.Store)))
                    ?? throw new InvalidOperationException("The actual analytics lifetime is absent.");
                await Assert.That(lease.Dependencies.SetEquals([lifetime])).IsTrue();
                await Assert.That(ledger.RetainedEntries).IsEqualTo(1);
                await Assert.That(ledger.RetainedEncodedBytes).IsEqualTo((long)OrleansSerialization.Measure(lifetime));
                var before = work.ReadBytes;
                var snapshot = fixture.Database.CaptureAnalyticsAggregateBound(D.Root, D.Query(fixture), work, lease);
                await Assert.That(ReferenceEquals(snapshot.Work, work)).IsTrue();
                await Assert.That(work.ReadBytes).IsGreaterThan(before);
                await AnalyticsStoreTestProjection.RunAsync(snapshot, new([D.Row(D.Alpha, 1, 2, 2, 2, 2)], 1), token);
                fixture.Database.ValidateAnalyticsAggregatePublication(snapshot);
                await Assert.That(ledger.RetainedEntries).IsEqualTo(1);
                await Assert.That(ledger.RetainedEncodedBytes).IsEqualTo((long)OrleansSerialization.Measure(lifetime));
            }), token);
}
