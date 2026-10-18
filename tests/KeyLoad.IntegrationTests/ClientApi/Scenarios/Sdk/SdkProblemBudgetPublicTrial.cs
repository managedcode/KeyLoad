using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

internal static class SdkProblemBudgetPublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async administrator =>
            {
                var prepared = await SdkProblemBudgetPublicPreparation.CreateAsync(fixture, administrator, token);
                return await SdkProblemBudgetPublicCallers.RunOwnedAsync(fixture, prepared.Identity.Secret, async caller =>
                {
                    await SdkProblemBudgetPublicFlow.RefusalsAsync(administrator, caller, prepared, token);
                    return await SdkProblemBudgetPublicFlow.PublishHealthyAsync(administrator, caller, prepared, token);
                }, token);
            }, token);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await RestartAsync(fixture, token);
            _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
                fixture.AdminKey, async administrator =>
                {
                    var current = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
                    await Assert.That(current.NodeId).IsEqualTo(state.Status.NodeId);
                    await Assert.That(current.Incarnation).IsEqualTo(state.Status.Incarnation);
                    await Assert.That(current.Position).IsGreaterThanOrEqualTo(state.Status.Position);
                    await Assert.That(current.ReadGeneration).IsGreaterThanOrEqualTo(state.Status.ReadGeneration);
                    return await SdkProblemBudgetPublicCallers.RunOwnedAsync(fixture, state.Identity.Secret, async caller =>
                    {
                        await SdkProblemBudgetPublicFlow.RefusalsAsync(administrator, caller, state, token);
                        await SdkProblemBudgetPublicFlow.ReplayHealthyAsync(administrator, caller, state, token);
                        return true;
                    }, token);
                }, token);
        }
    }

    private static async Task RestartAsync(ClusterFixture fixture, CancellationToken token)
    {
        string[] nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
        var failures = new List<Exception>();
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.KillServerAsync(node, "sdk-problem-budget-public-cold", token), failures);
        }
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await fixture.BeginProcessRestartAsync(node, token), failures);
        }
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartServerAsync(node, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
