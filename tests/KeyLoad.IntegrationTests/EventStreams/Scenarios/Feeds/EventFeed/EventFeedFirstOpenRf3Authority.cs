using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventFeedFirstOpenRf3Authority
{
    private const long RevokedEpoch = 2;
    private const long RepairedEpoch = 3;

    internal static Task RevokeAsync(KeyLoadClient administrator, CancellationToken token)
        => ChangeAsync(administrator, RevokedEpoch, false, token);

    internal static Task RepairAsync(KeyLoadClient administrator, CancellationToken token)
        => ChangeAsync(administrator, RepairedEpoch, true, token);

    private static async Task ChangeAsync(KeyLoadClient administrator, long epoch, bool allowed,
        CancellationToken token)
    {
        var expected = PartitionMovementPublicParentRf3Administrator.Definition(epoch, allowed);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(
            Guid.NewGuid(), expected, token));
        await Assert.That(actual.PolicyEpoch).IsEqualTo(epoch);
        await Assert.That(actual.ClusterAdministrator).IsEqualTo(allowed);
    }
}
