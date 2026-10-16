using KeyLoad.Core;
using P = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsSharedProtocol;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal sealed class AnalyticsSharedIntegrationTests
{
    [Test]
    public Task NativeMixedBatchBindsLinksAndRetainsAtomicRollbackOutboxAndReplay()
        => AnalyticsSharedFlow.MixedAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task NativeAnalyticsOnlyWriteRegistersRosterInventoryAndExportsBothCanonicalFamilies()
        => AnalyticsSharedFamilies.RunAsync(TestContext.Current!.Execution.CancellationToken);
    [Test]
    public async Task PolicyOnlySchemaCasCannotChangeAnalyticsStoreShape()
    {
        using var fixture = new TestDatabase(nativeReplicaAdmission: true);
        AnalyticsSharedFlow.Configure(fixture);
        var before = fixture.Store.Read(view => fixture.Database.Resource(view, fixture.Partition, P.Store));
        var replacement = before with
        {
            SchemaVersion = before.SchemaVersion + 1,
            AnalyticsSchema = new([P.Dimension], [P.Metric, P.OtherMetric])
        };
        var actual = fixture.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(
            fixture.Partition.TenantId, fixture.Partition.DatabaseId, replacement, before.SchemaVersion));
        await Assert.That(actual.Error).IsEqualTo(ErrorCode.UnsupportedCapability);
        await AnalyticsSharedAssertions.SameAsync(before,
            fixture.Store.Read(view => fixture.Database.Resource(view, fixture.Partition, P.Store)));
    }
}
