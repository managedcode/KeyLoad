using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class QueueDeadlineLeaderRf3Ownership(string originalLeaderNode, string originalVoter,
    string survivorNode, string[] survivors)
{
    private const string InvalidLeader = "The original native snapshots did not establish a unique owned leader.";
    internal string OriginalLeaderNode { get; } = originalLeaderNode;
    internal string SurvivorNode { get; } = survivorNode;

    internal static async Task<QueueDeadlineLeaderRf3Ownership> CaptureAsync(ClusterFixture fixture,
        CancellationToken token)
    {
        var captured = new List<QueueDeadlineLeaderRf3Observation>();
        foreach (var name in QueueLeaseRf3Protocol.Nodes)
        {
            captured.Add(await ReadAsync(fixture, name, token));
        }
        var leaders = captured.Select(x => x.Status.Leader).Distinct(StringComparer.Ordinal).ToArray();
        if (leaders.Length != 1 || leaders[0] is null)
        {
            throw new InvalidOperationException(InvalidLeader);
        }
        var owner = captured.Single(x => x.Voter == leaders[0]);
        var remaining = captured.Where(x => x.ResourceName != owner.ResourceName).Select(x => x.ResourceName).ToArray();
        await Assert.That(remaining.Length).IsEqualTo(2);
        return new(owner.ResourceName, owner.Voter, remaining[0], remaining);
    }

    internal async Task RequireReplacementAsync(ClusterFixture fixture, CancellationToken token)
    {
        var until = TimeProvider.System.GetUtcNow() + DueRecurringRf3Protocol.ProgressWindow;
        while (TimeProvider.System.GetUtcNow() < until)
        {
            var first = await SdkAsync(fixture, survivors[0], sdk => sdk.StatusAsync(token), token);
            var second = await SdkAsync(fixture, survivors[1], sdk => sdk.StatusAsync(token), token);
            if (first.IsSuccess && second.IsSuccess && first.Value is { RoutingReady: true, Voters: 3 } a
                && second.Value is { RoutingReady: true, Voters: 3 } b && a.Leader is not null
                && a.Leader == b.Leader && a.Leader != originalVoter)
            {
                var currentFirst = await ReadAsync(fixture, survivors[0], token);
                var currentSecond = await ReadAsync(fixture, survivors[1], token);
                if (currentFirst.Status.Leader == currentSecond.Status.Leader
                    && currentFirst.Status.Leader != originalVoter
                    && (currentFirst.Status.Leader == currentFirst.Voter || currentFirst.Status.Leader == currentSecond.Voter))
                {
                    return;
                }
            }
            await Task.Delay(DueRecurringRf3Protocol.PollInterval, TimeProvider.System, token);
        }
        throw new TimeoutException(InvalidLeader);
    }

    internal async Task RequireRejoinedAsync(ClusterFixture fixture, CancellationToken token)
    {
        var restored = await ReadAsync(fixture, OriginalLeaderNode, token);
        await Assert.That(restored.Voter).IsEqualTo(originalVoter);
        var current = await ReadAsync(fixture, SurvivorNode, token);
        await Assert.That(restored.Status.Leader).IsEqualTo(current.Status.Leader);
        await Assert.That(restored.Status.Leader).IsNotNull();
    }

    private static Task<QueueDeadlineLeaderRf3Observation> ReadAsync(ClusterFixture fixture,
        string node, CancellationToken token)
        => QueueDeadlineRf3Callers.UseAsync(fixture, node, fixture.AdminKey, async admin =>
        {
            var status = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.StatusAsync(token));
            var snapshot = (await McpCallerAssertions.SuccessAsync<AdminNodeSnapshot>(
                await admin.Mcp.CallWithoutBodyAsync(AdminDashboardProtocol.SnapshotTool, token))).Value;
            await Assert.That(status.RoutingReady).IsTrue();
            await Assert.That(status.Voters).IsEqualTo(3);
            await Assert.That(snapshot.Node.NodeId).IsEqualTo(status.NodeId);
            await Assert.That(snapshot.Node.Incarnation).IsEqualTo(status.Incarnation);
            await Assert.That(snapshot.Node.Leader).IsEqualTo(status.Leader);
            await Assert.That(snapshot.LocalVoter).IsNotNull();
            await Assert.That(snapshot.Voters.Length).IsEqualTo(3);
            await Assert.That(snapshot.Voters.Contains(snapshot.LocalVoter!)).IsTrue();
            return new QueueDeadlineLeaderRf3Observation(node, status, snapshot.LocalVoter!);
        }, token);

    private static async Task<T> SdkAsync<T>(ClusterFixture fixture, string node,
        Func<KeyLoadClient, Task<T>> read, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var failures = new List<Exception>();
        T? result = default;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var http = McpCallerHttp.Create(fixture, node);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
                result = await read(sdk);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result!;
    }

}
