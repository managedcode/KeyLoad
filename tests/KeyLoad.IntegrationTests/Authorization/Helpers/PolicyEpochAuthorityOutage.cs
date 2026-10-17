using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class PolicyEpochAuthorityOutage
{
    private const string Scenario = "policy-cursor-authority-unavailable";
    private static readonly string[] StoppedNodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node3];

    internal static async Task RunAsync(ClusterFixture fixture, RequestCqrsRf3Callers reader,
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
                await fixture.KillContainerAsync(node, Scenario, token);
            }
            await PolicyEpochAuthorityRefusals.AllAsync(reader, state, cursors, token);
        }, failures);
        using var cleanup = new CancellationTokenSource(RequestCqrsRf3Protocol.CleanupDeadline, TimeProvider.System);
        await RestoreAsync(fixture, stopped, failures, cleanup.Token);
        ServerFailureObserver.ThrowIfAny(failures);
        var after = await PolicyEpochPublicAssertions.ImageAsync(root, state.Scenario, token);
        await SqlRf3Protocol.EqualAsync(before.Rows.ToArray(), after.Rows.ToArray());
        await Assert.That(after.CutPosition).IsGreaterThanOrEqualTo(before.CutPosition);
        await SqlRf3Protocol.EqualAsync(outbox,
            await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(state.Scenario.Partition, token)));
        await PolicyEpochPublicRefusals.AllAsync(fixture, reader, root, state, ErrorCode.CursorExpired, token);
        await PolicyEpochPublicAssertions.PagesAsync(reader, root, state.Scenario, false, token);
        await PolicyEpochPublicPolicy.ReplayAsync(root, state, token);
        await SqlRf3Protocol.EqualAsync(outbox,
            await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(state.Scenario.Partition, token)));
    }

    private static async Task RestoreAsync(ClusterFixture fixture, HashSet<string> stopped,
        List<Exception> failures, CancellationToken token)
    {
        foreach (var node in stopped)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
                await fixture.BeginContainerRestartAsync(node, token), failures);
        }
        foreach (var node in stopped)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartContainerAsync(node, token), failures);
        }
    }
}
