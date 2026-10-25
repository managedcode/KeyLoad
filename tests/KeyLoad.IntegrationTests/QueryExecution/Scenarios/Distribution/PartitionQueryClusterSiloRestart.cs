using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.QueryExecution;

/// <summary>Owns the real silo interruption, survivor query and bounded rejoin cleanup.</summary>
internal static class PartitionQueryClusterSiloRestart
{
    private const string AddedId = "after-silo-restart";
    private const string AddedValue = "survivor-committed";
    private const string CleanupFailureKey = "KeyLoad.PartitionQuery.SiloRestartCleanup";

    internal static async Task RunAsync(ClusterFixture fixture, PartitionQueryClusterScenario scenario,
        CancellationToken cancellationToken)
    {
        var selectedSilo = McpCallerProtocol.Node3;
        var stopped = false;
        Exception? primary = null;
        try
        {
            await fixture.KillServerAsync(selectedSilo, PartitionQueryClusterProtocol.NodeFailureScenario,
                cancellationToken).ConfigureAwait(false);
            stopped = true;
            // Reads reach the surviving copies once Orleans membership on every survivor removed the killed silo.
            await ClusterMembershipReadiness.WaitForActiveSilosAsync(fixture,
                [McpCallerProtocol.Node1, McpCallerProtocol.Node2], cancellationToken).ConfigureAwait(false);
            using (var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1))
            {
                var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
                var page = await McpCallerAssertions.SdkSuccessAsync(await sdk.PartitionQueryAsync(
                    scenario.Request(), cancellationToken));
                await PartitionQueryClusterAssertions.AssertOracleAsync(page, scenario, scenario.Partitions);
            }
            await RestoreSiloAsync(fixture, selectedSilo, primary).ConfigureAwait(false);
            stopped = false;
            await VerifySurvivorReadWriteAsync(fixture, scenario, selectedSilo, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            if (stopped)
            {
                await RestoreSiloAsync(fixture, selectedSilo, primary).ConfigureAwait(false);
            }
        }
    }

    private static async Task VerifySurvivorReadWriteAsync(ClusterFixture fixture,
        PartitionQueryClusterScenario scenario, string selectedSilo, CancellationToken cancellationToken)
    {
        var survivor = new[] { McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3 }
            .First(node => !string.Equals(node, selectedSilo, StringComparison.Ordinal));
        using var http = McpCallerHttp.Create(fixture, survivor);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await ClusterReplicationTestSupport.EventuallyAsync(async () =>
        {
            var status = await sdk.StatusAsync(cancellationToken).ConfigureAwait(false);
            return status.IsSuccess && status.Value.RoutingReady;
        }, cancellationToken).ConfigureAwait(false);
        var target = scenario.Partitions[1];
        var row = new PartitionQueryClusterInputRow(target, AddedId, 0, AddedValue);
        var payload = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [PartitionQueryClusterProtocol.RankJsonProperty] = row.Rank,
            [PartitionQueryClusterProtocol.ValueAlias] = row.Value
        }, JsonDefaults.Options);
        var command = new CommandRequest(Guid.NewGuid(), target,
            [new PutDocument(PartitionQueryClusterProtocol.Collection, row.Id, payload, ExpectedRevision: 0)]);
        var committed = await ClusterReplicationTestSupport.RetryDuringSiloRecoveryAsync(
            () => sdk.CommitAsync(command, cancellationToken), cancellationToken).ConfigureAwait(false);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(committed).ConfigureAwait(false);
        await Assert.That(receipt!.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(ClusterFixtureProtocol.NodeCount);
        var updated = scenario with
        {
            Inputs = scenario.Inputs.Add(row)
        };
        var request = scenario.Request();
        var page = await McpCallerAssertions.SdkSuccessAsync(await sdk.PartitionQueryAsync(request, cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await PartitionQueryClusterAssertions.AssertOracleAsync(page!, updated, scenario.Partitions).ConfigureAwait(false);
    }

    private static async Task RestoreSiloAsync(ClusterFixture fixture, string selectedSilo, Exception? primary)
    {
        using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(45), TimeProvider.System);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => fixture.RestartServerAsync(selectedSilo, recovery.Token), failures)
            .ConfigureAwait(false);
        if (failures.Count == 0)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(selectedSilo,
                    WaitBehavior.WaitOnResourceUnavailable, recovery.Token).ConfigureAwait(false);
                await VerifyAllNodesReadyAsync(fixture, recovery.Token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        if (failures.Count == 0)
        {
            return;
        }
        if (primary is not null)
        {
            primary.Data[CleanupFailureKey] = new AggregateException(failures);
            return;
        }
        throw new AggregateException("Partition query silo-restart cleanup failed.", failures);
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
                return status.IsSuccess && status.Value.RoutingReady;
            }, cancellationToken).ConfigureAwait(false);
        }
    }
}
