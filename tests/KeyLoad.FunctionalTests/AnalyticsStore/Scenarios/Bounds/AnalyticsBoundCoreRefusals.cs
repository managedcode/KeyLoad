using D = KeyLoad.FunctionalTests.AnalyticsStore.AnalyticsStoreTestData;

namespace KeyLoad.FunctionalTests.AnalyticsStore;

internal static class AnalyticsBoundCoreRefusals
{
    internal static Task AuthorityAsync(CancellationToken token)
        => AnalyticsBoundCoreOwner.RunAsync(async (fixture, owner, original) =>
        {
            var principal = new PrincipalRecord(D.Reader, fixture.Partition.TenantId, [], []) { PolicyEpoch = 1 };
            _ = fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
            await AnalyticsBoundCoreOwner.LeaseAsync(fixture, owner, async (lease, ledger, work) =>
                await RuntimeAnalyticsBounds.UnchangedAsync(fixture, async () =>
                {
                    var error = Assert.ThrowsExactly<KeyLoadException>(() =>
                        fixture.Database.ReadAnalyticsEventsBound(D.Reader, D.Read(fixture), work, lease));
                    await Assert.That(error.Code).IsEqualTo(ErrorCode.PermissionDenied);
                    await Assert.That(ledger.RetainedEntries).IsEqualTo(0);
                    await Assert.That(lease.Dependencies.Count).IsEqualTo(0);
                }), original);
            await AnalyticsBoundCoreFlow.HealthyAsync(fixture, owner, original);
        }, token);

    internal static Task ResultBytesAsync(CancellationToken token)
        => AnalyticsBoundCoreOwner.RunAsync(async (fixture, owner, original) =>
        {
            var lifetime = fixture.Store.Read(view => (KeyLoad.Core.ResourceExecution.ResourceLifetimeCatalog.Read(view,
                fixture.Partition.TenantId, fixture.Partition.DatabaseId, D.Store,
                fixture.Database.Resource(view, fixture.Partition, D.Store))).Value)
                ?? throw new InvalidOperationException("The actual analytics lifetime is absent.");
            await AnalyticsBoundCoreOwner.LeaseAsync(fixture, owner, async (lease, ledger, work) =>
                await RuntimeAnalyticsBounds.UnchangedAsync(fixture, async () =>
                {
                    work.ConstrainResultBytes(checked((int)OrleansSerialization.Measure(lifetime) - 1));
                    var error = Assert.ThrowsExactly<KeyLoadException>(() =>
                        fixture.Database.ReadAnalyticsEventsBound(D.Root, D.Read(fixture), work, lease));
                    await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
                    await Assert.That(ledger.RetainedEntries).IsEqualTo(0);
                    await Assert.That(lease.Dependencies.Count).IsEqualTo(0);
                }), original);
            await AnalyticsBoundCoreFlow.HealthyAsync(fixture, owner, original);
        }, token);

    internal static Task BindingAsync(CancellationToken token)
        => AnalyticsBoundCoreOwner.RunAsync(async (fixture, owner, original) =>
        {
            await AnalyticsBoundCoreOwner.LeaseAsync(fixture, owner, async (lease, ledger, work) =>
                await AnalyticsBoundCoreOwner.LeaseAsync(fixture, owner, async (other, otherLedger, _) =>
                    await RuntimeAnalyticsBounds.UnchangedAsync(fixture, async () =>
                    {
                        Assert.ThrowsExactly<InvalidOperationException>(() =>
                            fixture.Database.ReadAnalyticsEventsBound(D.Root, D.Read(fixture), work, other));
                        await Assert.That(ledger.RetainedEntries).IsEqualTo(0);
                        await Assert.That(otherLedger.RetainedEntries).IsEqualTo(0);
                        await Assert.That(lease.Dependencies.Count).IsEqualTo(0);
                        await Assert.That(other.Dependencies.Count).IsEqualTo(0);
                        await Assert.That(fixture.Database.ReadAnalyticsEventsBound(D.Root, D.Read(fixture), work, lease).Events.Length)
                            .IsEqualTo(1);
                    }), original), original);
        }, token);
}
