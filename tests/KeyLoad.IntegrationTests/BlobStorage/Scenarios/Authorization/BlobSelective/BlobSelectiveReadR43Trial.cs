using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobSelectiveReadR43Trial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async admin =>
            {
                var versions = await BlobSelectiveReadR43Publication.CreateAsync(admin, token);
                var blob = versions[0].Begin.Blob;
                var identity = await McpPersistedIdentity.CreateAsync(fixture, blob.Partition, blob.Resource, Capability.BlobRead, token);
                await Assert.That(identity.Principal.PolicyEpoch).IsEqualTo(1L);
                var state = new BlobSelectiveReadR43State(versions, identity)
                {
                    Both = identity.Principal with
                    {
                        Grants = [new(blob.Partition.DatabaseId, blob.Resource, Capability.BlobRead),
                    new(blob.Partition.DatabaseId, BlobSelectiveReadR43Publication.SiblingResource, Capability.BlobRead)],
                        PolicyEpoch = 2L
                    }
                };
                await PersistAsync(admin, state, state.Both, token);
                return await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, identity.Secret,
                    async reader =>
                    {
                        await BlobSelectiveReadR43Reads.MatrixAsync(admin, reader, state, false, token);
                        await PersistAsync(admin, state, state.Both with
                        {
                            Grants = [state.Both.Grants[1]],
                            PolicyEpoch = 3L
                        }, token);
                        await BlobSelectiveReadR43Reads.MatrixAsync(admin, reader, state, true, token);
                        await PersistAsync(admin, state, state.Both with
                        {
                            PolicyEpoch = 4L
                        }, token);
                        await BlobSelectiveReadR43Reads.MatrixAsync(admin, reader, state, false, token);
                        await BlobSelectiveReadR43Replay.RunAsync(admin, state, token);
                        return true;
                    }, token);
            }, token);
    }
    private static async Task PersistAsync(RequestCqrsClusterCallers admin, BlobSelectiveReadR43State state,
        PrincipalRecord expected, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
        await SqlClusterProtocol.EqualAsync(expected, actual);
        state.Identity = state.Identity with
        {
            Principal = actual
        };
        state.Outbox = await BlobSelectiveReadR43Reads.OutboxAsync(admin, state, token);
    }
}
