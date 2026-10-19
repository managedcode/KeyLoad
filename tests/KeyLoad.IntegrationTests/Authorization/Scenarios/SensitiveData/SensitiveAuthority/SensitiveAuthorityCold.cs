using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.StorageReplication;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SensitiveAuthorityCold
{
    private static readonly string[] Nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];

    internal static async Task RunAsync(ClusterFixture fixture, SensitiveAuthorityState state, CancellationToken token)
    {
        var image = await ImageAsync(fixture, state, token);
        var before = await StatusAsync(fixture, token);
        var failures = new List<Exception>();
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.KillServerAsync(node, SensitiveAuthorityProtocol.ColdScenario, token), failures);
        }
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await fixture.BeginProcessRestartAsync(node, token), failures);
        }
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartServerAsync(node, token), failures);
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
        await SqlClusterProtocol.EqualAsync(image, await ImageAsync(fixture, state, token));
        await SensitiveAuthorityClients.RunAsync(fixture, state.Identity.Secret, async (root, reader, session) =>
        {
            await SensitiveAuthorityContinuation.CurrentAsync(root, reader, session, state, token);
            return state;
        }, token);
    }

    private static Task<SensitiveAuthorityImage> ImageAsync(ClusterFixture fixture,
        SensitiveAuthorityState state, CancellationToken token)
        => AdversarialAuthorityTrial.WithRootAsync(fixture, McpCallerProtocol.Node1,
            root => SensitiveAuthorityAssertions.ImageAsync(root, state.Scenario, true, token));

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
