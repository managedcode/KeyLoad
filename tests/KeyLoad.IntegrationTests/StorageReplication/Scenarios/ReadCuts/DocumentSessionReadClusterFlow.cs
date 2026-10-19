using Aspire.Hosting.Testing;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.Server;
using static KeyLoad.IntegrationTests.Features.StorageReplication.DocumentSessionReadClusterProtocol;

namespace KeyLoad.IntegrationTests.Features.StorageReplication;

internal static class DocumentSessionReadClusterFlow
{
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
            await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigureResourceAsync(Guid.NewGuid(),
                new(Tenant, Database, new(Collection, ResourceKind.Collection, Collection)), deadline.Token));
            var command = new CommandRequest(Guid.NewGuid(), partition, [new PutDocument(Collection, DocumentId, FirstJson)]);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(command, deadline.Token));
            await DocumentSessionReceiptAssertions.ReceiptAsync(receipt, command, First);
            var identity = await McpPersistedIdentity.CreateAsync(fixture, partition, Collection,
                Capability.DocumentsRead, deadline.Token);
            var selected = McpCallerProtocol.Node3;
            stopped.Add(selected);
            await fixture.KillServerAsync(selected, KillScenario, deadline.Token);
            var survivors = new[] { McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3 }
                .Where(node => node != selected).ToArray();
            await VerifySurvivorsAsync(fixture, survivors, partition, command, receipt, identity, stopped, deadline.Token);
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
        CommandRequest command, CommitReceipt receipt, McpPersistedIdentity identity, HashSet<string> stopped,
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
        await DocumentSessionReadClusterAssertions.HealthyAsync(sdk, mcp, reference, receipt.Token, FirstJson, First, token);
        await DocumentSessionReadClusterAssertions.RejectedAsync(sdk, mcp, reference, receipt.Token, identity.Secret, token);
        foreach (var node in stopped.ToArray())
        {
            await fixture.RestartServerAsync(node, token);
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(node, token);
            stopped.Remove(node);
        }
        await DocumentSessionReadClusterAuthorization.VerifyAsync(admin, sdk, mcp, reference, receipt.Token, identity, token);
        var nextCommand = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(Collection, DocumentId, SecondJson, First, ExplicitReplacement: true)]);
        var next = await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(nextCommand, token));
        await DocumentSessionReceiptAssertions.ReceiptAsync(next, nextCommand, Second);
        await Assert.That(next.PersistedCopies).IsEqualTo(3);
        await Assert.That(next.Token.Incarnation).IsEqualTo(receipt.Token.Incarnation);
        await Assert.That(next.Token.AtomicPartitionId).IsEqualTo(receipt.Token.AtomicPartitionId);
        await DocumentSessionReadClusterAssertions.HealthyAsync(sdk, mcp, reference, receipt.Token, SecondJson, Second, token);
        stopped.Add(survivors[First]);
        await fixture.KillServerAsync(survivors[First], KillScenario, token);
        stopped.Add(McpCallerProtocol.Node3);
        await fixture.KillServerAsync(McpCallerProtocol.Node3, KillScenario, token);
        await DocumentSessionReadClusterAssertions.HealthyAsync(sdk, mcp, reference, next.Token, SecondJson, Second, token);
        var unavailableReplay = await admin.CommitAsync(nextCommand, token);
        await Assert.That(unavailableReplay.IsFailed).IsTrue();
        await Assert.That(unavailableReplay.Value).IsNull();
        await Assert.That(unavailableReplay.Problem?.ErrorCode is nameof(ErrorCode.OwnershipLost)
            or nameof(ErrorCode.UnknownWriteOutcome)).IsTrue();
        foreach (var node in stopped.ToArray())
        {
            await fixture.RestartServerAsync(node, token);
            stopped.Remove(node);
        }
        await using var restoredAdminMcp = await McpOfficialClient.ConnectAsync(fixture, survivors[Zero], fixture.AdminKey, token);
        await DocumentSessionReceiptAssertions.ReplayedAsync(admin, restoredAdminMcp, command, receipt, token);
        await DocumentSessionReceiptAssertions.ReplayedAsync(admin, restoredAdminMcp, nextCommand, next, token);
        await DocumentSessionReadClusterAssertions.HealthyAsync(sdk, mcp, reference, receipt.Token, SecondJson, Second, token);
    }
}
