using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobReadPolicyPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async administrator =>
            {
                var published = await BlobReadPolicyPublicPublication.CreateAsync(administrator, token);
                var blob = published.Begin.Blob;
                var identity = await McpPersistedIdentity.CreateAsync(fixture, blob.Partition, blob.Resource,
                    Capability.BlobRead, token);
                await Assert.That(identity.Principal.PolicyEpoch).IsEqualTo(1L);
                return await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                    identity.Secret, async reader =>
                    {
                        await BlobReadPolicyPublicAssertions.HealthyAsync(reader, published, token);
                        await PersistAsync(administrator, identity.Principal with { Grants = [], PolicyEpoch = 2L }, token);
                        for (var repeat = 0; repeat < 2; repeat++)
                        { await BlobReadPolicyPublicAssertions.DeniedAsync(reader, administrator, published, identity.Secret, token); }
                        await PersistAsync(administrator, identity.Principal with { PolicyEpoch = 3L }, token);
                        await BlobReadPolicyPublicAssertions.HealthyAsync(reader, published, token);
                        await BlobSupersessionPublicAssertions.ReplayAsync(administrator, published, token);
                        await BlobReadPolicyPublicAssertions.HealthyAsync(reader, published, token);
                        return true;
                    }, token);
            }, token);
        await BlobSelectiveReadR43Trial.RunAsync(fixture, token);
    }

    private static async Task PersistAsync(RequestCqrsRf3Callers administrator,
        PrincipalRecord expected, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(
            Guid.NewGuid(), expected, token));
        await SqlRf3Protocol.EqualAsync(expected, actual);
    }
}
