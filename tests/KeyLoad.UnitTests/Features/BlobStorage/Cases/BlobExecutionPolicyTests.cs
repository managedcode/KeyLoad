using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobExecutionPolicyTests
{
    [Test]
    public async Task ConfiguredCatalogProofRejectsUnexaminedCatalogWithoutCreatingQuota()
    {
        using var fixture = new TestDatabase(blobExecution: new() { InitialCatalogProofRecords = 1 });
        fixture.Configure("first", ResourceKind.Collection);
        fixture.Configure("second", ResourceKind.Collection);
        var rejected = fixture.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(fixture.Partition.TenantId, fixture.Partition.DatabaseId,
                new("blob-policy", ResourceKind.BlobStore, fixture.Partition.TransactionDomainId)));
        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.BudgetExceeded);
        var catalogKey = KeySpace.Resource(fixture.Partition.TenantId, fixture.Partition.DatabaseId, "blob-policy");
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(catalogKey))).IsNull();
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(KeyCodec.Encode("blob-global-v1")))).IsNull();

        var healthy = new DatabaseEngine(fixture.Store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(),
            UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(),
            UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(),
            UnitExecutionOptions.BlobExecution(new() { InitialCatalogProofRecords = 2 }), UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution());
        var request = new ConfigureResourceRequest(fixture.Partition.TenantId, fixture.Partition.DatabaseId,
            new("blob-policy", ResourceKind.BlobStore, fixture.Partition.TransactionDomainId));
        var operation = new ReplicatedOperation(Guid.NewGuid(), OperationKind.ConfigureResource, "root",
            TimeProvider.System.GetUtcNow(), System.Text.Json.JsonSerializer.Serialize(request, JsonDefaults.Options));
        var result = healthy.Apply(operation).Get<ResourceDefinition>();
        await Assert.That(result.Name).IsEqualTo("blob-policy");
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(catalogKey))).IsNotNull();
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(KeyCodec.Encode("blob-global-v1")))).IsNotNull();
    }

    [Test]
    [Arguments(0, 1, 1)]
    [Arguments(129, 1, 1)]
    [Arguments(1, 0, 1)]
    [Arguments(1, 10_001, 1)]
    [Arguments(1, 1, 0)]
    [Arguments(1, 1, 4_194_305)]
    public async Task InvalidPolicyRejectsBeforeAnyStoreDirectory(int pageSize, int proofRecords, long pageBytes)
    {
        var directory = Path.Combine(Path.GetTempPath(), "keyload-invalid-blob-options-" + Guid.NewGuid().ToString("N"));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            using var unexpected = new TestDatabase(directory: directory,
                blobExecution: new() { MetadataPageSize = pageSize, InitialCatalogProofRecords = proofRecords, RestorePageBytes = pageBytes });
        });
        await Assert.That(Directory.Exists(directory)).IsFalse();
    }
}
