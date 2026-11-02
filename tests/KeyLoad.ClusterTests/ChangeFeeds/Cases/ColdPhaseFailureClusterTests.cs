using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.ChangeFeeds;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class ColdPhaseFailureClusterTests(ClusterFixture fixture)
{
    private static readonly string[] Nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
    private const string Collection = "documents";
    private const string DocumentId = "kept";
    private const string Json = "{\"value\":1}";
    private const string FailureScenario = "cold-phase-existing-kill";

    [Test]
    public async Task FailedColdKillPhaseStartsNoReplacementsThenExplicitRecoveryPreservesDataAndReceipt()
    {
        using var deadline = McpCallerDeadline.Create();
        var original = await SeedAsync(deadline.Token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await fixture.KillServerAsync(Nodes[0], FailureScenario, deadline.Token);
            InvalidOperationException? observed = null;
            try
            {
                await FeedLiveClusterCold.RestartAsync(fixture, deadline.Token);
            }
            catch (InvalidOperationException failure)
            {
                observed = failure;
            }
            await Assert.That(observed?.Message).IsEqualTo(ProcessRestartOwnership.PendingFailure);
            foreach (var node in Nodes)
            {
                await Assert.That(fixture.RequireProcessRuntime().IsKilledWithoutReplacement(node)).IsTrue();
            }
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => RestoreKilledAsync(deadline.Token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await VerifyHealthyAsync(original.Command, original.Receipt, deadline.Token);
    }

    private async Task<(CommandRequest Command, CommitReceipt Receipt)> SeedAsync(CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, Nodes[0]);
        var client = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var partition = new PartitionRef("cold-phase", "database", "domain", Guid.NewGuid().ToString("N"));
        _ = ClusterReplicationTestSupport.Success(await client.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, new(Collection, ResourceKind.Collection,
                partition.TransactionDomainId)), token));
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(Collection, DocumentId, Json, ExpectedRevision: 0)]);
        return (command, ClusterReplicationTestSupport.Success(await client.CommitAsync(command, token)));
    }

    private async Task RestoreKilledAsync(CancellationToken token)
    {
        var killed = Nodes.Where(fixture.RequireProcessRuntime().IsKilledWithoutReplacement).ToArray();
        foreach (var node in killed)
        {
            await fixture.BeginProcessRestartAsync(node, token);
        }
        foreach (var node in killed)
        {
            await fixture.RestartServerAsync(node, token);
        }
        await ClusterMembershipReadiness.WaitForActiveSilosAsync(fixture, Nodes, token);
    }

    private async Task VerifyHealthyAsync(CommandRequest command, CommitReceipt original, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, Nodes[1]);
        var client = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var replay = ClusterReplicationTestSupport.Success(await client.CommitAsync(command, token));
        await SubmissionColdAssertions.ReceiptAsync(replay, original);
        var read = ClusterReplicationTestSupport.Success(await client.GetAsync(
            new(command.Partition, Collection, DocumentId), original.Token, token));
        await Assert.That(read.Document?.Revision).IsEqualTo(1);
        await Assert.That(read.Document?.Json).IsEqualTo(Json);
    }
}
