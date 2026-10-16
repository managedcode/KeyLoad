using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryRf3Policy
{
    internal static async Task SetAsync(TwoRf3MembershipWave wave, long epoch, bool administrator, CancellationToken token)
    {
        var principal = PartitionMovementPublicParentRf3Administrator.Definition(epoch, administrator);
        await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey,
            async root =>
            {
                await SqlRf3Protocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
                    await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token).ConfigureAwait(false)));
                return true;
            }, token).ConfigureAwait(false);
        await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey,
            async root =>
            {
                await SqlRf3Protocol.EqualAsync(principal,
                    await ResourcePartitionInventoryRf3Metadata.PrincipalAsync(root, principal, token).ConfigureAwait(false));
                return true;
            }, token).ConfigureAwait(false);
    }
}
