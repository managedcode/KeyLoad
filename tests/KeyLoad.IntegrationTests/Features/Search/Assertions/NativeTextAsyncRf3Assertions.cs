using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextAsyncRf3Assertions
{
    private const int ReplicaCount = 3;
    private const string MissingLeader = "The RF3 cluster did not report one shared leader.";

    internal static async Task AssertSearchAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SearchRequest request, (string Id, double Score)[] expected, bool verifyRedaction,
        CancellationToken cancellationToken)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.SearchAsync(request, cancellationToken));
        var official = await McpCallerAssertions.SuccessAsync<RankedDocument[]>(await mcp.CallAsync(
            McpCallerTools.SearchExecute, request, cancellationToken));
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(actual[index].Document.Reference.Id).IsEqualTo(expected[index].Id);
            await Assert.That(actual[index].Score).IsEqualTo(expected[index].Score);
            if (verifyRedaction)
            {
                await Assert.That(actual[index].Document.Redacted).IsTrue();
                await Assert.That(actual[index].Document.RedactedFields)
                    .Contains(NativeTextRf3Scenario.SecretField);
                await Assert.That(actual[index].Document.Json.Contains(NativeTextRf3Scenario.Secret,
                    StringComparison.Ordinal)).IsFalse();
            }
        }
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(official.Value))).IsTrue();
    }

    internal static async Task AssertClusterHealthyAsync(ClusterFixture fixture,
        KeyLoadClient[] administrators, McpOfficialClient officialAdministrator,
        CancellationToken cancellationToken)
    {
        var resourceNames = new[] { McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3 };
        await Task.WhenAll(resourceNames.Select(name => fixture.App.ResourceNotifications
            .WaitForResourceHealthyAsync(name, cancellationToken)));
        var results = await Task.WhenAll(administrators.Select(client => client.StatusAsync(cancellationToken)));
        var statuses = await Task.WhenAll(results.Select(result => McpCallerAssertions.SdkSuccessAsync(result)));
        await Assert.That(statuses.Length).IsEqualTo(ReplicaCount);
        await Assert.That(statuses.All(status => status.RoutingReady)).IsTrue();
        await Assert.That(statuses.All(status => status.Voters == ReplicaCount)).IsTrue();
        var leader = statuses[0].Leader ?? throw new InvalidOperationException(MissingLeader);
        await Assert.That(statuses.All(status => string.Equals(status.Leader, leader,
            StringComparison.Ordinal))).IsTrue();
        var official = await McpCallerAssertions.SuccessAsync<NodeStatus>(await officialAdministrator.Client
            .CallToolAsync(McpCallerTools.AdminStatus, cancellationToken: cancellationToken));
        await Assert.That(official.Value.NodeId).IsEqualTo(statuses[0].NodeId);
        await Assert.That(official.Value.Incarnation).IsEqualTo(statuses[0].Incarnation);
        await Assert.That(official.Value.ProcessId).IsEqualTo(statuses[0].ProcessId);
        await Assert.That(official.Value.RoutingReady).IsTrue();
        await Assert.That(official.Value.Voters).IsEqualTo(ReplicaCount);
        await Assert.That(official.Value.Leader).IsEqualTo(leader);
    }
}
