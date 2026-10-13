using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPrincipalRevokedR44Policy
{
    internal static async Task ConfigureAsync(RequestCqrsRf3Callers admin, BlobPrincipalRevokedR44State state,
        bool revoked, CancellationToken token)
    {
        var expected = state.Identity.Principal with { Revoked = revoked, PolicyEpoch = revoked ? 2L : 3L };
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
        state.Outbox = await BlobLiveListR43Pages.OutboxAsync(admin, state.Published.Begin.Blob.Partition, token);
        await BlobPrincipalRevokedR44Pages.CurrentAsync(admin, state, token);
    }
}
