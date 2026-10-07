using Aspire.Hosting.Testing;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.DocumentSessionReadRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class DocumentSessionReadRf3Flow
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
            await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
            var identity = await McpPersistedIdentity.CreateAsync(fixture, partition, Collection,
                Capability.DocumentsRead, deadline.Token);
            var status = await McpCallerAssertions.SdkSuccessAsync(await admin.StatusAsync(deadline.Token));
            var oldLeader = new Uri(status.Leader!).Host;
            stopped.Add(oldLeader);
            await fixture.KillContainerAsync(oldLeader, KillScenario, deadline.Token);
            var survivors = new[] { McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3 }
                .Where(node => node != oldLeader).ToArray();
            await VerifySurvivorsAsync(fixture, survivors, partition, command, receipt, identity, stopped, deadline.Token);
        }, failures).ConfigureAwait(false);
        using var cleanup = new CancellationTokenSource(RequestCqrsRf3Protocol.CleanupDeadline, TimeProvider.System);
        foreach (var node in stopped)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartContainerAsync(node, cleanup.Token), failures)
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
        var replay = await ClusterReplicationTestSupport.RetryDuringElectionAsync(() => admin.CommitAsync(command, token), token);
        var replayed = await McpCallerAssertions.SdkSuccessAsync(replay);
        await Assert.That(NativeSerialization.Serialize(replayed).SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
        using var sdkHttp = fixture.App.CreateHttpClient(survivors[Zero], ClusterFixtureProtocol.HttpEndpointName);
        sdkHttp.Timeout = ClusterFixtureProtocol.ClientTimeout;
        var sdk = new KeyLoadClient(sdkHttp, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, survivors[Zero], identity.Secret, token);
        var reference = new EntityRef(partition, Collection, DocumentId);
        await DocumentSessionReadRf3Assertions.HealthyAsync(sdk, mcp, reference, receipt.Token, FirstJson, First, token);
        await DocumentSessionReadRf3Assertions.RejectedAsync(sdk, mcp, reference, receipt.Token, identity.Secret, token);
        await DocumentSessionReadRf3Authorization.VerifyAsync(admin, sdk, mcp, reference, receipt.Token, identity, token);
        var next = await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(new(Guid.NewGuid(), partition,
            [new PutDocument(Collection, DocumentId, SecondJson, First, ExplicitReplacement: true)]), token));
        await Assert.That(next.Token.OwnershipEpoch).IsEqualTo(receipt.Token.OwnershipEpoch);
        await Assert.That(next.Token.Incarnation).IsEqualTo(receipt.Token.Incarnation);
        await Assert.That(next.Token.AtomicPartitionId).IsEqualTo(receipt.Token.AtomicPartitionId);
        await DocumentSessionReadRf3Assertions.HealthyAsync(sdk, mcp, reference, receipt.Token, SecondJson, Second, token);
        stopped.Add(survivors[First]);
        await fixture.KillContainerAsync(survivors[First], KillScenario, token);
        await DocumentSessionReadRf3NoQuorum.VerifyAsync(sdk, mcp, reference, receipt.Token, identity.Secret, token);
        foreach (var node in stopped.ToArray())
        { await fixture.RestartContainerAsync(node, token); stopped.Remove(node); }
        await DocumentSessionReadRf3Assertions.HealthyAsync(sdk, mcp, reference, receipt.Token, SecondJson, Second, token);
    }
}
