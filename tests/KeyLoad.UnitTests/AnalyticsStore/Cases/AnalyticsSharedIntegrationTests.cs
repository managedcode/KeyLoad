using P = KeyLoad.UnitTests.AnalyticsStore.AnalyticsSharedProtocol;

namespace KeyLoad.UnitTests.AnalyticsStore;

internal sealed class AnalyticsSharedIntegrationTests
{
    [Test]
    public Task MixedBatchBindsLinksAndRetainsAtomicRollbackOutboxAndReplay()
        => AnalyticsSharedFlow.MixedAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task AnalyticsOnlyWriteRegistersPartitionAndExportsBothCanonicalFamilies()
        => AnalyticsSharedFamilies.RunAsync(TestContext.Current!.Execution.CancellationToken);
    [Test]
    public async Task PolicyOnlySchemaCasCannotChangeAnalyticsStoreShape()
    {
        using var fixture = new TestDatabase();
        AnalyticsSharedFlow.Configure(fixture);
        var before = fixture.Store.Read(view => fixture.Database.Resource(view, fixture.Partition, P.Store));
        var replacement = before with
        {
            SchemaVersion = before.SchemaVersion + 1,
            AnalyticsSchema = new([P.Dimension], [P.Metric, P.OtherMetric])
        };
        var actual = fixture.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(
            fixture.Partition.TenantId, fixture.Partition.DatabaseId, replacement)
        {
            ExpectedSchemaVersion = before.SchemaVersion
        });
        await Assert.That(Errors.Code(actual.Problem)).IsEqualTo(ErrorCode.UnsupportedCapability);
        await AnalyticsSharedAssertions.SameAsync(before,
            fixture.Store.Read(view => fixture.Database.Resource(view, fixture.Partition, P.Store)));
    }
}
