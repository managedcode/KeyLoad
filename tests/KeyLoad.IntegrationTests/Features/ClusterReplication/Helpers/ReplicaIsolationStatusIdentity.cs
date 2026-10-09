using Aspire.Hosting.Testing;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Binds persisted physical node identities to actual authenticated endpoints across the owned fault.</summary>
internal static class ReplicaIsolationStatusIdentity
{
    internal static async Task<IReadOnlyDictionary<string, NodeStatus>> CaptureAsync(ClusterFixture fixture,
        IReadOnlyList<string> resources, CancellationToken cancellationToken)
    {
        var identities = new Dictionary<string, NodeStatus>(StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            using var clientHttp = fixture.App.CreateHttpClient(resource, ClusterFixtureProtocol.HttpEndpointName);
            clientHttp.Timeout = ClusterFixtureProtocol.ClientTimeout;
            var client = new KeyLoadClient(clientHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
            var status = await McpCallerAssertions.SdkSuccessAsync(await client.StatusAsync(cancellationToken));
            await Assert.That(Guid.TryParse(status.NodeId, out var identity) && identity != Guid.Empty).IsTrue();
            await Assert.That(status.ReadGeneration >= ReplicaIsolationFlowProtocol.Zero).IsTrue();
            identities.Add(resource, status);
        }
        await Assert.That(identities.Values.Select(status => status.NodeId).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(resources.Count);
        return identities;
    }

    internal static async Task VerifyOfficialAsync(ClusterFixture fixture, string resource, NodeStatus original,
        Guid incarnation, long minimumTerm, CancellationToken cancellationToken)
    {
        await using var client = await McpOfficialClient.ConnectAsync(fixture, resource, fixture.AdminKey, cancellationToken);
        var status = await McpCallerAssertions.SuccessAsync<NodeStatus>(await client.CallWithoutBodyAsync(
            McpCallerTools.AdminStatus, cancellationToken));
        await ReplicaIsolationFlowAssertions.StatusAsync(status.Value, original.NodeId, resource, incarnation, original.ReadGeneration, minimumTerm);
    }

    internal static async Task VerifyRestoredAsync(ClusterFixture fixture, IReadOnlyDictionary<string, NodeStatus> identities,
        Guid incarnation, CancellationToken cancellationToken)
    {
        foreach (var pair in identities)
        { await VerifyRestoredNodeAsync(fixture, pair.Key, pair.Value, identities, incarnation, cancellationToken); }
    }

    private static async Task VerifyRestoredNodeAsync(ClusterFixture fixture, string resource, NodeStatus original,
        IReadOnlyDictionary<string, NodeStatus> identities, Guid incarnation, CancellationToken cancellationToken)
    {
        using var clientHttp = fixture.App.CreateHttpClient(resource, ClusterFixtureProtocol.HttpEndpointName);
        clientHttp.Timeout = ClusterFixtureProtocol.ClientTimeout;
        var client = new KeyLoadClient(clientHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        var status = await McpCallerAssertions.SdkSuccessAsync(await client.StatusAsync(cancellationToken));
        var leader = new Uri(status.Leader!).Host;
        await Assert.That(identities.ContainsKey(leader)).IsTrue();
        await ReplicaIsolationFlowAssertions.StatusAsync(status, original.NodeId, leader, incarnation, original.ReadGeneration, status.ConsensusTerm);
        await using var official = await McpOfficialClient.ConnectAsync(fixture, resource, fixture.AdminKey, cancellationToken);
        var native = await McpCallerAssertions.SuccessAsync<NodeStatus>(await official.CallWithoutBodyAsync(
            McpCallerTools.AdminStatus, cancellationToken));
        await ReplicaIsolationFlowAssertions.StatusAsync(native.Value, original.NodeId, leader, incarnation, original.ReadGeneration, status.ConsensusTerm);
    }
}
