using Aspire.Hosting.Testing;
using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.ResourceExecution;
using KeyLoad.Server;
using static KeyLoad.ClusterTests.StorageReplication.DocumentSessionReadClusterProtocol;

namespace KeyLoad.ClusterTests.StorageReplication;

internal static class DocumentSessionReadClusterFlow
{
    private static readonly string[] Nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];

    internal static async Task RunAsync(ClusterFixture fixture)
    {
        using var deadline = McpCallerDeadline.Create();
        var stopped = new HashSet<string>(StringComparer.Ordinal);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var adminHttp = fixture.App.CreateHttpClient(McpCallerProtocol.Node1, ClusterFixtureProtocol.HttpEndpointName);
            adminHttp.Timeout = ClusterFixtureProtocol.ClientTimeout;
            var admin = new KeyLoadClient(adminHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
            var partition = new PartitionRef(Tenant, Database, Collection, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
            var creationId = Guid.NewGuid();
            var creation = new ConfigureResourceRequest(Tenant, Database, new(Collection, ResourceKind.Collection, Collection));
            await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigureResourceAsync(creationId, creation, deadline.Token));
            var command = new CommandRequest(Guid.NewGuid(), partition, [new PutDocument(Collection, DocumentId, FirstJson)]);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(command, deadline.Token));
            await DocumentSessionReceiptAssertions.ReceiptAsync(receipt, command, First);
            var identity = await McpPersistedIdentity.CreateAsync(fixture, partition, Collection,
                Capability.DocumentsRead, deadline.Token);
            var selected = McpCallerProtocol.Node3;
            stopped.Add(selected);
            await fixture.KillServerAsync(selected, KillScenario, deadline.Token);
            var survivors = Nodes.Where(node => node != selected).ToArray();
            await ClusterMembershipReadiness.WaitForActiveSilosAsync(fixture, survivors, deadline.Token);
            await VerifySurvivorsAsync(fixture, survivors, partition, ResourceCreationLifetime.Create(creationId, creation),
                command, receipt, identity, stopped, deadline.Token);
        }, failures).ConfigureAwait(false);
        using var cleanup = new CancellationTokenSource(RequestCqrsClusterProtocol.CleanupDeadline, TimeProvider.System);
        foreach (var node in stopped)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartServerAsync(node, cleanup.Token), failures)
                .ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task VerifySurvivorsAsync(ClusterFixture fixture, string[] survivors, PartitionRef partition,
        EntityRefLifetime lifetime, CommandRequest command, CommitReceipt receipt, McpPersistedIdentity identity, HashSet<string> stopped,
        CancellationToken token)
    {
        using var adminHttp = fixture.App.CreateHttpClient(survivors[Zero], ClusterFixtureProtocol.HttpEndpointName);
        adminHttp.Timeout = ClusterFixtureProtocol.ClientTimeout;
        var admin = new KeyLoadClient(adminHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        var replay = await admin.CommitAsync(command, token);
        await Assert.That(replay.IsFailed).IsTrue();
        await Assert.That(replay.Value).IsNull();
        await Assert.That(replay.Problem?.ErrorCode is nameof(ErrorCode.OwnershipLost)
            or nameof(ErrorCode.UnknownWriteOutcome)).IsTrue();
        using var sdkHttp = fixture.App.CreateHttpClient(survivors[Zero], ClusterFixtureProtocol.HttpEndpointName);
        sdkHttp.Timeout = ClusterFixtureProtocol.ClientTimeout;
        var sdk = new KeyLoadClient(sdkHttp, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, survivors[Zero], identity.Secret, token);
        var reference = new EntityRef(partition, Collection, DocumentId);
        var stored = reference with { Lifetime = lifetime };
        await DocumentSessionReadClusterAssertions.HealthyAsync(sdk, mcp, reference, stored, receipt.Token, FirstJson, First, token);
        await DocumentSessionReadClusterAssertions.RejectedAsync(sdk, mcp, reference, stored, receipt.Token, identity.Secret, token);
        await RestartAsync(fixture, stopped, token);
        await DocumentSessionReadClusterAuthorization.VerifyAsync(admin, sdk, mcp, reference, stored, receipt.Token, identity, token);
        var nextCommand = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(Collection, DocumentId, SecondJson, First, ExplicitReplacement: true)]);
        var next = await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(nextCommand, token));
        await DocumentSessionReceiptAssertions.ReceiptAsync(next, nextCommand, Second);
        await Assert.That(next.PersistedCopies).IsEqualTo(3);
        await Assert.That(next.Token.Incarnation).IsEqualTo(receipt.Token.Incarnation);
        await Assert.That(next.Token.AtomicPartitionId).IsEqualTo(receipt.Token.AtomicPartitionId);
        await DocumentSessionReadClusterAssertions.HealthyAsync(sdk, mcp, reference, stored, receipt.Token, SecondJson, Second, token);
        // Only one of the three silos stops, so the remaining majority can remove it from the membership.
        stopped.Add(survivors[First]);
        await fixture.KillServerAsync(survivors[First], KillScenario, token);
        await ClusterMembershipReadiness.WaitForActiveSilosAsync(fixture,
            Nodes.Where(node => node != survivors[First]).ToArray(), token);
        await DocumentSessionReadClusterAssertions.HealthyAsync(sdk, mcp, reference, stored, next.Token, SecondJson, Second, token);
        var unavailableReplay = await admin.CommitAsync(nextCommand, token);
        await Assert.That(unavailableReplay.IsFailed).IsTrue();
        await Assert.That(unavailableReplay.Value).IsNull();
        await Assert.That(unavailableReplay.Problem?.ErrorCode is nameof(ErrorCode.OwnershipLost)
            or nameof(ErrorCode.UnknownWriteOutcome)).IsTrue();
        await RestartAsync(fixture, stopped, token);
        await using var restoredAdminMcp = await McpOfficialClient.ConnectAsync(fixture, survivors[Zero], fixture.AdminKey, token);
        await DocumentSessionReceiptAssertions.ReplayedAsync(admin, restoredAdminMcp, command, receipt, token);
        await DocumentSessionReceiptAssertions.ReplayedAsync(admin, restoredAdminMcp, nextCommand, next, token);
        await DocumentSessionReadClusterAssertions.HealthyAsync(sdk, mcp, reference, stored, receipt.Token, SecondJson, Second, token);
    }

    /// <summary>Restarts every stopped node and waits until all nodes report each other active, because a write requires
    /// every configured copy.</summary>
    private static async Task RestartAsync(ClusterFixture fixture, HashSet<string> stopped, CancellationToken token)
    {
        foreach (var node in stopped.ToArray())
        {
            await fixture.RestartServerAsync(node, token);
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(node, token);
            stopped.Remove(node);
        }
        await ClusterMembershipReadiness.WaitForActiveSilosAsync(fixture, Nodes, token);
    }
}
