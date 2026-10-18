using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class CredentialAuthorityCold
{
    private const string ColdScenario = "credential-current-policy-cold";
    private static readonly string[] Nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];

    internal static async Task RunAsync(ClusterFixture fixture, CredentialAuthorityState state, bool expire, CancellationToken token)
    {
        var image = await AdversarialAuthorityTrial.WithRootAsync(fixture, McpCallerProtocol.Node1,
            root => DiscoveryAuthorityValues.ImageAsync(root, state.Document, token));
        await RestartAsync(fixture, token);
        await SqlClusterProtocol.EqualAsync(image, await AdversarialAuthorityTrial.WithRootAsync(fixture, McpCallerProtocol.Node1,
            root => DiscoveryAuthorityValues.ImageAsync(root, state.Document, token)));
        await CredentialAuthorityClients.RunAsync(fixture, state, expire, changeCredential: false, token);
    }

    internal static async Task RestartAsync(ClusterFixture fixture, CancellationToken token)
    {
        var before = await StatusAsync(fixture, token);
        var failures = new List<Exception>();
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.KillContainerAsync(node, ColdScenario, token), failures);
        }
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await fixture.BeginContainerRestartAsync(node, token), failures);
        }
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartContainerAsync(node, token), failures);
        }
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(
            node, WaitBehavior.WaitOnResourceUnavailable, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        var after = await StatusAsync(fixture, token);
        foreach (var node in Nodes)
        {
            await Assert.That(after[node].NodeId).IsEqualTo(before[node].NodeId);
            await Assert.That(after[node].Incarnation).IsEqualTo(before[node].Incarnation);
            await Assert.That(after[node].ReadGeneration).IsGreaterThanOrEqualTo(before[node].ReadGeneration);
            await Assert.That(after[node].Position).IsGreaterThanOrEqualTo(before[node].Position);
            await Assert.That(after[node].RoutingReady).IsTrue();
        }
    }

    private static async Task<Dictionary<string, NodeStatus>> StatusAsync(ClusterFixture fixture, CancellationToken token)
    {
        var result = new Dictionary<string, NodeStatus>(StringComparer.Ordinal);
        foreach (var node in Nodes)
        {
            result.Add(node, await AdversarialAuthorityTrial.WithRootAsync(fixture, node,
                async root => await McpCallerAssertions.SdkSuccessAsync(await root.StatusAsync(token))));
        }
        return result;
    }
}
