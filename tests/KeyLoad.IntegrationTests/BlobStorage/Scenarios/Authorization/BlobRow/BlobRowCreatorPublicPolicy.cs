using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobRowCreatorPublicPolicy
{
    internal static async Task<McpPersistedIdentity> CreateAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers administrator, BlobRef blob, CancellationToken token)
    {
        var identity = await McpPersistedIdentity.CreateAsync(fixture, blob.Partition, blob.Resource,
            Capability.BlobRead | Capability.BlobWrite, token);
        var expected = identity.Principal with
        {
            OwnerId = "blob-owner-" + Guid.NewGuid().ToString("N"),
            RestrictRows = true,
            PolicyEpoch = 2L
        };
        var actual = await PersistAsync(administrator, expected, token);
        await Assert.That(actual.ClusterAdministrator).IsFalse();
        return identity with
        {
            Principal = actual
        };
    }

    internal static async Task<PrincipalRecord> PersistAsync(RequestCqrsClusterCallers administrator,
        PrincipalRecord expected, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(
            Guid.NewGuid(), expected, token));
        await SqlClusterProtocol.EqualAsync(expected, actual);
        return actual;
    }
}
