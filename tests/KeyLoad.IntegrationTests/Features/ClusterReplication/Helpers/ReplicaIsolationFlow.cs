using Aspire.Hosting.Testing;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.DocumentSessionReadRf3Protocol;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Exercises original ACK, real namespace partition, one new majority ACK, strict refusal and restored literal reads.</summary>
internal static class ReplicaIsolationFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken cancellationToken)
    {
        var owner = fixture.RequireReplicaIsolation();
        using var setupHttp = fixture.App.CreateHttpClient(McpCallerProtocol.Node1, ClusterFixtureProtocol.HttpEndpointName);
        setupHttp.Timeout = ClusterFixtureProtocol.ClientTimeout;
        var setup = new KeyLoadClient(setupHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        var partition = new PartitionRef(Tenant, Database, Collection, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        await McpCallerAssertions.SdkSuccessAsync(await setup.ConfigureResourceAsync(Guid.NewGuid(),
            new(Tenant, Database, new(Collection, ResourceKind.Collection, Collection)), cancellationToken));
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition, Collection,
            Capability.DocumentsRead, cancellationToken);
        var command = new CommandRequest(Guid.NewGuid(), partition, [new PutDocument(Collection, DocumentId, FirstJson)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await setup.CommitAsync(command, cancellationToken));
        await ReplicaIsolationFlowAssertions.ReceiptAsync(receipt, command, First);
        var identities = await ReplicaIsolationStatusIdentity.CaptureAsync(fixture, owner.Resources, cancellationToken);
        var firstStatus = await McpCallerAssertions.SdkSuccessAsync(await setup.StatusAsync(cancellationToken));
        var isolated = new Uri(firstStatus.Leader!).Host;
        await Assert.That(owner.Resources.Contains(isolated, StringComparer.Ordinal)).IsTrue();
        using var oldAdminHttp = fixture.App.CreateHttpClient(isolated, ClusterFixtureProtocol.HttpEndpointName);
        oldAdminHttp.Timeout = ClusterFixtureProtocol.ClientTimeout;
        var oldAdmin = new KeyLoadClient(oldAdminHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        var originalStatus = await McpCallerAssertions.SdkSuccessAsync(await oldAdmin.StatusAsync(cancellationToken));
        await Assert.That(originalStatus.ConsensusTerm > Zero).IsTrue();
        await ReplicaIsolationFlowAssertions.StatusAsync(originalStatus, identities[isolated], isolated, receipt.Token.Incarnation,
            originalStatus.ConsensusTerm);
        using var oldSdkHttp = fixture.App.CreateHttpClient(isolated, ClusterFixtureProtocol.HttpEndpointName);
        oldSdkHttp.Timeout = ClusterFixtureProtocol.ClientTimeout;
        var oldSdk = new KeyLoadClient(oldSdkHttp, identity.Secret, IntegrationClientOptions.Execution());
        await using var oldMcp = await McpOfficialClient.ConnectAsync(fixture, isolated, identity.Secret, cancellationToken);
        await using var oldAdminMcp = await McpOfficialClient.ConnectAsync(fixture, isolated, fixture.AdminKey, cancellationToken);
        var nativeStatus = await McpCallerAssertions.SuccessAsync<NodeStatus>(await oldAdminMcp.CallAsync(
            McpCallerTools.AdminStatus, new { }, cancellationToken));
        await ReplicaIsolationFlowAssertions.StatusAsync(nativeStatus.Value, identities[isolated], isolated, receipt.Token.Incarnation,
            originalStatus.ConsensusTerm);
        var reference = new EntityRef(partition, Collection, DocumentId);
        await DocumentSessionReadRf3Assertions.HealthyAsync(oldSdk, oldMcp, reference, receipt.Token, FirstJson, First, cancellationToken);
        await owner.IsolateAsync(isolated, cancellationToken);
        var authority = await ReplicaIsolationAuthorityReadiness.ObserveAsync(fixture, isolated,
            originalStatus.ConsensusTerm, owner, cancellationToken);
        using var majorityHttp = fixture.App.CreateHttpClient(authority.Leader, ClusterFixtureProtocol.HttpEndpointName);
        majorityHttp.Timeout = ClusterFixtureProtocol.ClientTimeout;
        var majority = new KeyLoadClient(majorityHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        var nextCommand = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(Collection, DocumentId, SecondJson, First, ExplicitReplacement: true)]);
        var next = await McpCallerAssertions.SdkSuccessAsync(await majority.CommitAsync(nextCommand, cancellationToken));
        await ReplicaIsolationFlowAssertions.ReceiptAsync(next, nextCommand, Second);
        var after = await McpCallerAssertions.SdkSuccessAsync(await majority.StatusAsync(cancellationToken));
        await Assert.That(after.ConsensusTerm > originalStatus.ConsensusTerm).IsTrue();
        await ReplicaIsolationFlowAssertions.StatusAsync(after, identities[authority.Leader], authority.Leader,
            receipt.Token.Incarnation, authority.Status.ConsensusTerm);
        await ReplicaIsolationStatusIdentity.VerifyOfficialAsync(fixture, authority.Leader, identities[authority.Leader],
            receipt.Token.Incarnation, after.ConsensusTerm, cancellationToken);
        await ReplicaIsolationFlowAssertions.RefusedAsync(oldSdk, oldMcp, reference, next.Token, identity.Secret, cancellationToken);
        await owner.VerifyObservedFaultAsync(cancellationToken);
        await owner.RestoreAsync(cancellationToken);
        await VerifyRestoredAsync(fixture, owner, reference, receipt, command, next, nextCommand, identity, identities, cancellationToken);
    }

    private static async Task VerifyRestoredAsync(ClusterFixture fixture, ReplicaIsolationOwner owner, EntityRef reference,
        CommitReceipt original, CommandRequest originalCommand, CommitReceipt next, CommandRequest nextCommand,
        McpPersistedIdentity identity, IReadOnlyDictionary<string, string> identities, CancellationToken cancellationToken)
    {
        foreach (var node in owner.Resources)
        { await VerifyNodeAsync(fixture, node, reference, original, originalCommand, next, nextCommand, identity, cancellationToken); }
        await ReplicaIsolationStatusIdentity.VerifyRestoredAsync(fixture, identities, next.Token.Incarnation, cancellationToken);
        await owner.VerifyRestoredNodesAsync(cancellationToken);
    }

    private static async Task VerifyNodeAsync(ClusterFixture fixture, string node, EntityRef reference,
        CommitReceipt original, CommandRequest originalCommand, CommitReceipt next, CommandRequest nextCommand,
        McpPersistedIdentity identity, CancellationToken cancellationToken)
    {
        using var sdkHttp = fixture.App.CreateHttpClient(node, ClusterFixtureProtocol.HttpEndpointName);
        sdkHttp.Timeout = ClusterFixtureProtocol.ClientTimeout;
        var sdk = new KeyLoadClient(sdkHttp, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, node, identity.Secret, cancellationToken);
        await DocumentSessionReadRf3Assertions.HealthyAsync(sdk, mcp, reference, original.Token, SecondJson, Second, cancellationToken);
        await DocumentSessionReadRf3Assertions.HealthyAsync(sdk, mcp, reference, next.Token, SecondJson, Second, cancellationToken);
        using var adminHttp = fixture.App.CreateHttpClient(node, ClusterFixtureProtocol.HttpEndpointName);
        adminHttp.Timeout = ClusterFixtureProtocol.ClientTimeout;
        var admin = new KeyLoadClient(adminHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        await ReplicaIsolationFlowAssertions.ReplayedAsync(admin, originalCommand, original, cancellationToken);
        await ReplicaIsolationFlowAssertions.ReplayedAsync(admin, nextCommand, next, cancellationToken);
        await DocumentSessionReadRf3Assertions.HealthyAsync(sdk, mcp, reference, next.Token, SecondJson, Second, cancellationToken);
    }
}
