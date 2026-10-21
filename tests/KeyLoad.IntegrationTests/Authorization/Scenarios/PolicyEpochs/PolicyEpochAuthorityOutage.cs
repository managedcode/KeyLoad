using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class PolicyEpochAuthorityOutage
{
    private const string Scenario = "policy-cursor-authority-unavailable";
    private static readonly string[] StoppedNodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node3];

    internal static async Task RunAsync(ClusterFixture fixture, RequestCqrsClusterCallers reader,
        KeyLoadClient root, PolicyEpochPublicState state, CancellationToken token)
    {
        var cursors = await PolicyEpochPublicAssertions.PagesAsync(reader, root, state.Scenario, false, token);
        var before = await PolicyEpochPublicAssertions.ImageAsync(root, state.Scenario, token);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(state.Scenario.Partition, token));
        var stopped = new HashSet<string>(StringComparer.Ordinal);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            foreach (var node in StoppedNodes)
            {
                stopped.Add(node);
                await fixture.KillServerAsync(node, Scenario, token);
            }
            await PolicyEpochMembership.WaitForSurvivorAsync(fixture, token);
            await ReadAvailableAsync(reader, state, cursors, token);
            await ClusterReplicationTestSupport.MissingCopiesAsync(await root.CommitAsync(state.Seed, token));
            var available = await PolicyEpochPublicAssertions.ImageAsync(root, state.Scenario, token);
            await SqlClusterProtocol.EqualAsync(before.Rows.ToArray(), available.Rows.ToArray());
            await Assert.That(available.CutPosition).IsGreaterThanOrEqualTo(before.CutPosition);
            await SqlClusterProtocol.EqualAsync(outbox,
                await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(state.Scenario.Partition, token)));
        }, failures);
        using var cleanup = new CancellationTokenSource(RequestCqrsClusterProtocol.CleanupDeadline, TimeProvider.System);
        await RestoreAsync(fixture, stopped, failures, cleanup.Token);
        ServerFailureObserver.ThrowIfAny(failures);
        var after = await PolicyEpochPublicAssertions.ImageAsync(root, state.Scenario, token);
        await SqlClusterProtocol.EqualAsync(before.Rows.ToArray(), after.Rows.ToArray());
        await Assert.That(after.CutPosition).IsGreaterThanOrEqualTo(before.CutPosition);
        await SqlClusterProtocol.EqualAsync(outbox,
            await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(state.Scenario.Partition, token)));
        await PolicyEpochPublicRefusals.AllAsync(fixture, reader, root, state, ErrorCode.CursorExpired, token);
        await PolicyEpochPublicAssertions.PagesAsync(reader, root, state.Scenario, false, token);
        await PolicyEpochPublicPolicy.ReplayAsync(root, state, token);
        await SqlClusterProtocol.EqualAsync(outbox,
            await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(state.Scenario.Partition, token)));
    }

    private static async Task ReadAvailableAsync(RequestCqrsClusterCallers reader, PolicyEpochPublicState state,
        string[] cursors, CancellationToken token)
    {
        for (var route = 0; route < PolicyEpochPublicProtocol.RouteCount; route++)
        {
            var request = PolicyEpochPublicProtocol.Request(state.Scenario.Partition, cursors[route]);
            var page = await PolicyEpochPublicCalls.ReadAsync(reader, request, route, token);
            QueryRow[] expected = [new(PolicyEpochPublicProtocol.SecondId, PolicyEpochPublicProtocol.SeedRevision,
                PolicyEpochPublicProtocol.SecondJson, false, [])];
            await SqlClusterProtocol.EqualAsync(expected, page.Rows.ToArray());
            await Assert.That(page.Cursor).IsNull();
            await Assert.That(page.AccessPath).IsEqualTo(PolicyEpochPublicProtocol.Scan);
            await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(state.Receipt.Token.Position);
        }
    }

    private static async Task RestoreAsync(ClusterFixture fixture, HashSet<string> stopped,
        List<Exception> failures, CancellationToken token)
    {
        foreach (var node in stopped)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
                await fixture.BeginProcessRestartAsync(node, token), failures);
        }
        foreach (var node in stopped)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartServerAsync(node, token), failures);
        }
    }
}
