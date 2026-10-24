using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Search;

internal static class ProjectionPublicPolicyCold
{
    internal static async Task RunAsync(ClusterFixture fixture, EventProjectionClusterScenario scenario,
        McpPersistedIdentity reader, CommitReceipt original, CancellationToken token)
    {
        var before = await CaptureAsync(fixture, scenario, reader, token);
        await RestartAsync(fixture, token);
        var after = await CaptureAsync(fixture, scenario, reader, token);
        for (var node = 0; node < before.Length; node++)
        {
            await Assert.That(after[node].Node.NodeId).IsEqualTo(before[node].Node.NodeId);
            await Assert.That(after[node].Node.Incarnation).IsEqualTo(before[node].Node.Incarnation);
            await Assert.That(after[node].Node.ReadGeneration).IsGreaterThanOrEqualTo(before[node].Node.ReadGeneration);
            await Assert.That(after[node].Node.Position).IsGreaterThanOrEqualTo(before[node].Node.Position);
            await Assert.That(after[node].PhysicalCut).IsGreaterThanOrEqualTo(before[node].PhysicalCut);
        }
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            reader.Secret, async caller =>
            {
                await EventProjectionClusterAssertions.VerifyVectorBothAsync(caller.Sdk, caller.Mcp,
                    scenario.VectorSearch(), reader.Secret, ProjectionPublicPolicyProtocol.Healthy(), token);
                return true;
            }, token);
        await ReplayAsync(fixture, scenario, original, token);
        await CaptureAsync(fixture, scenario, reader, token);
    }

    internal static async Task ReplayAsync(ClusterFixture fixture, EventProjectionClusterScenario scenario,
        CommitReceipt original, CancellationToken token)
    {
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            scenario.WorkerSecret, async worker =>
            {
                var sdk = await McpCallerAssertions.SdkSuccessAsync(
                    await worker.Sdk.CommitAsync(scenario.ApplyCommand(), token));
                var response = await worker.Mcp.CallAsync(McpCallerTools.DocumentsCommit, scenario.ApplyCommand(), token);
                var mcp = await McpCallerAssertions.SuccessAsync<CommitReceipt>(response);
                await Assert.That(global::KeyLoad.JsonDefaults.Serialize(sdk).AsSpan().SequenceEqual(global::KeyLoad.JsonDefaults.Serialize(original))).IsTrue();
                await Assert.That(global::KeyLoad.JsonDefaults.Serialize(mcp.Value).AsSpan().SequenceEqual(global::KeyLoad.JsonDefaults.Serialize(original))).IsTrue();
                await McpCallerAssertions.DoesNotDiscloseAsync(response, scenario.WorkerSecret, EventProjectionClusterScenario.Secret);
                return true;
            }, token);
    }

    private static async Task<(NodeStatus Node, long PhysicalCut)[]> CaptureAsync(ClusterFixture fixture,
        EventProjectionClusterScenario scenario, McpPersistedIdentity reader, CancellationToken token)
    {
        var tasks = ProjectionPublicPolicyProtocol.Nodes
            .Select(node => CaptureNodeAsync(fixture, node, scenario, reader, token)).ToArray();
        await ((Task)Task.WhenAll(tasks)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        var failures = new List<Exception>();
        foreach (var task in tasks)
        {
            await ServerFailureObserver.ObserveAsync(() => task, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        var result = new List<(NodeStatus Node, long PhysicalCut)>();
        foreach (var task in tasks)
        {
            result.Add(await task);
        }
        return result.ToArray();
    }

    private static Task<(NodeStatus Node, long PhysicalCut)> CaptureNodeAsync(ClusterFixture fixture,
        string node, EventProjectionClusterScenario scenario, McpPersistedIdentity reader, CancellationToken token)
    {
        return RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, node, fixture.AdminKey,
            async root =>
            {
                var image = await ProjectionPublicPolicyImage.ReadAsync(root.Sdk, scenario, token);
                await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, node, reader.Secret,
                    async caller =>
                    {
                        await EventProjectionClusterAssertions.VerifyVectorBothAsync(caller.Sdk, caller.Mcp,
                            scenario.VectorSearch(), reader.Secret, ProjectionPublicPolicyProtocol.Healthy(), token);
                        return true;
                    }, token);
                var runtimeStatus = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.StatusAsync(token));
                return (runtimeStatus, image.CutPosition);
            }, token);
    }

    private static async Task RestartAsync(ClusterFixture fixture, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var node in ProjectionPublicPolicyProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.KillServerAsync(node, ProjectionPublicPolicyProtocol.ColdScenario, token), failures);
        }
        foreach (var node in ProjectionPublicPolicyProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await fixture.BeginProcessRestartAsync(node, token), failures);
        }
        foreach (var node in ProjectionPublicPolicyProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartServerAsync(node, token), failures);
        }
        foreach (var node in ProjectionPublicPolicyProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await fixture.App.ResourceNotifications
            .WaitForResourceHealthyAsync(node, Aspire.Hosting.ApplicationModel.WaitBehavior.WaitOnResourceUnavailable, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
