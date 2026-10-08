using Aspire.Hosting.Testing;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Binds persisted physical node identities to actual authenticated endpoints across the owned fault.</summary>
internal static class ReplicaIsolationStatusIdentity
{
    internal static async Task<IReadOnlyDictionary<string, string>> CaptureAsync(ClusterFixture fixture,
        IReadOnlyList<string> resources, CancellationToken cancellationToken)
    {
        var identities = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            using var clientHttp = fixture.App.CreateHttpClient(resource, ClusterFixtureProtocol.HttpEndpointName);
            clientHttp.Timeout = ClusterFixtureProtocol.ClientTimeout;
            var client = new KeyLoadClient(clientHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
            var status = await McpCallerAssertions.SdkSuccessAsync(await client.StatusAsync(cancellationToken));
            await Assert.That(Guid.TryParse(status.NodeId, out var identity) && identity != Guid.Empty).IsTrue();
            identities.Add(resource, status.NodeId);
        }
        await Assert.That(identities.Values.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(resources.Count);
        return identities;
    }

    internal static async Task VerifyOfficialAsync(ClusterFixture fixture, string resource, string nodeId,
        Guid incarnation, long minimumTerm, CancellationToken cancellationToken)
    {
        await using var client = await McpOfficialClient.ConnectAsync(fixture, resource, fixture.AdminKey, cancellationToken);
        var status = await McpCallerAssertions.SuccessAsync<NodeStatus>(await client.CallAsync(
            McpCallerTools.AdminStatus, new { }, cancellationToken));
        await ReplicaIsolationFlowAssertions.StatusAsync(status.Value, nodeId, resource, incarnation, minimumTerm);
    }

    internal static async Task VerifyRestoredAsync(ClusterFixture fixture, IReadOnlyDictionary<string, string> identities,
        Guid incarnation, CancellationToken cancellationToken)
    {
        foreach (var pair in identities)
        { await VerifyRestoredNodeAsync(fixture, pair.Key, pair.Value, identities, incarnation, cancellationToken); }
    }

    private static async Task VerifyRestoredNodeAsync(ClusterFixture fixture, string resource, string nodeId,
        IReadOnlyDictionary<string, string> identities, Guid incarnation, CancellationToken cancellationToken)
    {
        using var clientHttp = fixture.App.CreateHttpClient(resource, ClusterFixtureProtocol.HttpEndpointName);
        clientHttp.Timeout = ClusterFixtureProtocol.ClientTimeout;
        var client = new KeyLoadClient(clientHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        var status = await McpCallerAssertions.SdkSuccessAsync(await client.StatusAsync(cancellationToken));
        var leader = new Uri(status.Leader!).Host;
        await Assert.That(identities.ContainsKey(leader)).IsTrue();
        await ReplicaIsolationFlowAssertions.StatusAsync(status, nodeId, leader, incarnation, status.ConsensusTerm);
        await using var official = await McpOfficialClient.ConnectAsync(fixture, resource, fixture.AdminKey, cancellationToken);
        var native = await McpCallerAssertions.SuccessAsync<NodeStatus>(await official.CallAsync(
            McpCallerTools.AdminStatus, new { }, cancellationToken));
        await ReplicaIsolationFlowAssertions.StatusAsync(native.Value, nodeId, leader, incarnation, status.ConsensusTerm);
    }
}
