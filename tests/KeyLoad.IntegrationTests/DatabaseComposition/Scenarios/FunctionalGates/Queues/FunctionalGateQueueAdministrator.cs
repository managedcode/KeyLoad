using System.Globalization;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

/// <summary>Creates the real tenant-local privileged operator required by execution queue lifecycle administration.</summary>
internal static class FunctionalGateQueueAdministrator
{
    private const int ResourceCount = 3;
    private const string Prefix = "gate_queue_";
    private const Capability Grants = Capability.QueueInspect | Capability.DeadLettersRead | Capability.DeadLettersRedrive;

    internal static async Task<RequestCqrsClusterCallers> CreateAsync(FunctionalGateFixture fixture,
        RequestCqrsClusterCallers administrator, PartitionRef partition, CancellationToken token)
    {
        var identity = await McpPersistedIdentity.CreateAsync(administrator.Sdk, partition,
            Prefix + "0", Capability.None, token);
        var expected = identity.Principal with
        {
            ClusterAdministrator = true,
            PolicyEpoch = checked(identity.Principal.PolicyEpoch + 1),
            Grants = [.. Enumerable.Range(0, ResourceCount).Select(index => new ScopeGrant(partition.DatabaseId,
                Prefix + index.ToString(CultureInfo.InvariantCulture), Grants))]
        };
        var actual = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
        await SqlClusterProtocol.EqualAsync(expected, actual);
        return await RequestCqrsClusterCallers.ConnectAsync(fixture.App, fixture.NodeNames[0], identity.Secret, token);
    }
}
