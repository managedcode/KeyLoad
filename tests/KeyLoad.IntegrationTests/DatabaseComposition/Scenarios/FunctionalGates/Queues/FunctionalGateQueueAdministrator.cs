using System.Globalization;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Creates the real tenant-local privileged operator required by native queue lifecycle administration.</summary>
internal static class FunctionalGateQueueAdministrator
{
    private const int ResourceCount = 3;
    private const string Prefix = "gate_queue_";
    private const Capability Grants = Capability.QueueInspect | Capability.DeadLettersRead | Capability.DeadLettersRedrive;

    internal static async Task<RequestCqrsRf3Callers> CreateAsync(FunctionalGateFixture fixture,
        RequestCqrsRf3Callers administrator, PartitionRef partition, CancellationToken token)
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
        await SqlRf3Protocol.EqualAsync(expected, actual);
        return await RequestCqrsRf3Callers.ConnectAsync(fixture.App, fixture.NodeNames[0], identity.Secret, token);
    }
}
