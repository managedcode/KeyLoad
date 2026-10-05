using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>Owns the real leader interruption, survivor query and bounded rejoin cleanup.</summary>
internal static class PartitionQueryRf3Leadership
{
    private const string AddedId = "after-leader-loss";
    private const int AddedRank = 0;
    private const string AddedValue = "survivor-committed";
    private const string CleanupFailureKey = "KeyLoad.PartitionQuery.LeaderLossCleanup";

    internal static async Task RunAsync(ClusterFixture fixture, PartitionQueryRf3Scenario scenario,
        CancellationToken cancellationToken)
    {
        var leader = await FindLeaderNodeAsync(fixture, cancellationToken).ConfigureAwait(false);
        var stopped = false;
        Exception? primary = null;
        try
        {
            await fixture.KillContainerAsync(leader, PartitionQueryRf3Protocol.NodeFailureScenario,
                cancellationToken).ConfigureAwait(false);
            stopped = true;
            await VerifySurvivorReadWriteAsync(fixture, scenario, leader, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            if (stopped)
            { await RestoreLeaderAsync(fixture, leader, primary).ConfigureAwait(false); }
        }
    }

    private static async Task<string> FindLeaderNodeAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var status = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        var leader = new Uri(status!.Leader!).Host;
        var nodes = new[] { McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3 };
        await Assert.That(nodes.Contains(leader, StringComparer.Ordinal)).IsTrue();
        return leader;
    }

    private static async Task VerifySurvivorReadWriteAsync(ClusterFixture fixture,
        PartitionQueryRf3Scenario scenario, string leader, CancellationToken cancellationToken)
    {
        var survivor = new[] { McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3 }
            .First(node => !string.Equals(node, leader, StringComparison.Ordinal));
        using var http = McpCallerHttp.Create(fixture, survivor);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await ClusterReplicationTestSupport.EventuallyAsync(async () =>
        {
            var status = await sdk.StatusAsync(cancellationToken).ConfigureAwait(false);
            return status.IsSuccess && status.Value!.RoutingReady;
        }, cancellationToken).ConfigureAwait(false);
        var target = scenario.Partitions[1];
        var row = new PartitionQueryRf3InputRow(target, AddedId, AddedRank, AddedValue);
        var payload = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [PartitionQueryRf3Protocol.RankJsonProperty] = row.Rank,
            [PartitionQueryRf3Protocol.ValueAlias] = row.Value
        }, JsonDefaults.Options);
        var command = new CommandRequest(Guid.NewGuid(), target,
            [new PutDocument(PartitionQueryRf3Protocol.Collection, row.Id, payload, ExpectedRevision: 0)]);
        var committed = await ClusterReplicationTestSupport.RetryDuringElectionAsync(
            () => sdk.CommitAsync(command, cancellationToken), cancellationToken).ConfigureAwait(false);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(committed).ConfigureAwait(false);
        await Assert.That(receipt!.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        var updated = scenario with { Inputs = scenario.Inputs.Add(row) };
        var request = scenario.Request();
        var page = await McpCallerAssertions.SdkSuccessAsync(await sdk.PartitionQueryAsync(request, cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await PartitionQueryRf3Assertions.AssertOracleAsync(page!, updated, scenario.Partitions).ConfigureAwait(false);
    }

    private static async Task RestoreLeaderAsync(ClusterFixture fixture, string leader, Exception? primary)
    {
        using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => fixture.RestartContainerAsync(leader, recovery.Token), failures)
            .ConfigureAwait(false);
        if (failures.Count == 0)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(leader,
                    WaitBehavior.WaitOnResourceUnavailable, recovery.Token).ConfigureAwait(false);
                await VerifyAllNodesReadyAsync(fixture, recovery.Token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        if (failures.Count == 0)
        { return; }
        if (primary is not null)
        { primary.Data[CleanupFailureKey] = new AggregateException(failures); return; }
        throw new AggregateException("Partition query leader-loss cleanup failed.", failures);
    }

    private static async Task VerifyAllNodesReadyAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        foreach (var node in new[] { McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3 })
        {
            using var http = McpCallerHttp.Create(fixture, node);
            var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
            await ClusterReplicationTestSupport.EventuallyAsync(async () =>
            {
                var status = await sdk.StatusAsync(cancellationToken).ConfigureAwait(false);
                return status.IsSuccess && status.Value!.RoutingReady;
            }, cancellationToken).ConfigureAwait(false);
        }
    }
}
