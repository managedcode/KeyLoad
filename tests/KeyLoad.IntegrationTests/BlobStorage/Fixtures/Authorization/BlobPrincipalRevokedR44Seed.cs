using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPrincipalRevokedR44Seed
{
    private const string TenantPrefix = "blob-principal-revoked-r44-";
    private const string Database = "principal-db";
    private const string Domain = "principal-domain";
    private const string PartitionKey = "primary";
    private const string Resource = "principal-multipart";
    private const string Name = "two-parts";
    internal static async Task<BlobPrincipalRevokedR44State> CreateAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers admin, CancellationToken token)
    {
        var partition = new PartitionRef(TenantPrefix + Guid.NewGuid().ToString("N"), Database, Domain, PartitionKey);
        var resource = new ResourceDefinition(Resource, ResourceKind.BlobStore, Domain)
        {
            BlobPolicy = new()
            {
                MaxBlobBytes = 65553L,
                MaxReservedBytes = 65553L,
                MaxObjectKeys = 1,
                MaxVersions = 1,
                MaxUploads = 1
            }
        };
        await SqlClusterProtocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, Database, resource), token)));
        var published = await BlobBoundedRetirementPublicPublication.CreateAsync(admin, new(partition, Resource, Name),
            0L, BlobPrincipalRevokedR44Corpus.Bytes(0L, BlobPrincipalRevokedR44Corpus.Length), token);
        await BlobPrincipalRevokedR44Publication.RequireAsync(published);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition, Resource, Capability.BlobRead, token);
        await Assert.That(identity.Principal.PolicyEpoch).IsEqualTo(1L);
        var outbox = await BlobLiveListR43Pages.OutboxAsync(admin, partition, token);
        var state = new BlobPrincipalRevokedR44State(published, identity, outbox);
        await BlobPrincipalRevokedR44Pages.CurrentAsync(admin, state, token);
        return state;
    }
}
