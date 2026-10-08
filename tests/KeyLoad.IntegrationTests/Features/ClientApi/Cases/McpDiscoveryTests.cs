using System.Net;
using KeyLoad.Query;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>AC-MCPGW-001/002/004: compact discovery resolves canonical capabilities through the genuine official client.</summary>
/// <param name="fixture">The initialized actual Docker/Aspire RF3 application.</param>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class McpDiscoveryTests(ClusterFixture fixture)
{
    /// <summary>Both native listing APIs expose three meta tools and graph search retains every canonical schema.</summary>
    [Test]
    public async Task AcMcp001OfficialClientDiscoversMetaToolsAndSearchesCanonicalSchemasAndHints()
    {
        using var deadline = McpCallerDeadline.Create();
        await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node1,
            fixture.AdminKey, deadline.Token);
        var discovered = new HashSet<string>(StringComparer.Ordinal);
        var page = await session.Client.ListToolsAsync(new ListToolsRequestParams(), deadline.Token);
        await Assert.That(page.NextCursor is null).IsTrue();
        await McpGatewayDiscoveryAssertions.VerifyAsync(page.Tools);
        var aggregate = await session.Client.ListToolsAsync(cancellationToken: deadline.Token);
        await Assert.That(aggregate.Count).IsEqualTo(McpCallerProtocol.InitialToolCount);
        await McpGatewayDiscoveryAssertions.VerifyAsync(aggregate.Select(tool => tool.ProtocolTool));
        foreach (var expected in McpCatalogExpectations.Entries)
        {
            var tool = await session.Client.DiscoverKeyLoadToolAsync(expected.Name, deadline.Token);
            await Assert.That(discovered.Add(tool.Name)).IsTrue();
            if (tool.Name == McpCallerProtocol.GraphShortestPath)
            { await NativeMcpSchemaEvidence.RetainGraphShortestPathAsync(tool, deadline.Token); }
            await McpDiscoveryAssertions.VerifyAsync(tool);
        }
        await McpDiscoveryAssertions.VerifyInventoryAsync(discovered);
    }

    /// <summary>Removed direct names, unknown targets and recursive targets fail before an operation is dispatched.</summary>
    [Test]
    public async Task AcMcpGw001RemovedAndRecursiveToolsFailBeforeExecution()
    {
        using var deadline = McpCallerDeadline.Create();
        await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node1,
            fixture.AdminKey, deadline.Token);
        var direct = await session.Client.CallToolAsync(McpCallerTools.QueryCapabilities, cancellationToken: deadline.Token);
        await McpCallerAssertions.ErrorAsync(direct, ErrorCode.UnsupportedCapability, dispatched: false);
        foreach (var target in new[] { McpCallerProtocol.GatewayInvoke, "missing-canonical-operation" })
        {
            var recursive = await session.Client.InvokeKeyLoadToolAsync(target, cancellationToken: deadline.Token);
            await McpCallerAssertions.ErrorAsync(recursive, ErrorCode.UnsupportedCapability, dispatched: false);
        }
    }

    /// <summary>No-body official calls decode the canonical capability manifest and receive separate execution identities.</summary>
    [Test]
    public async Task AcMcp007OfficialClientReceivesCanonicalCapabilitiesAndFreshExecutionIds()
    {
        using var deadline = McpCallerDeadline.Create();
        await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            fixture.AdminKey, deadline.Token);
        var first = await McpCallerAssertions.SuccessAsync<QueryCapabilityManifest>(await session.Client.InvokeKeyLoadToolAsync(
            McpCallerTools.QueryCapabilities, cancellationToken: deadline.Token));
        var second = await McpCallerAssertions.SuccessAsync<QueryCapabilityManifest>(await session.Client.InvokeKeyLoadToolAsync(
            McpCallerTools.QueryCapabilities, cancellationToken: deadline.Token));
        await Assert.That(first.Value.AstVersion).IsEqualTo(McpCallerProtocol.AstVersion);
        await Assert.That(first.Value.ReadOnly).IsTrue();
        await Assert.That(JsonDefaults.Serialize(first.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(second.Value))).IsTrue();
        await Assert.That(first.RequestId).IsNotEqualTo(second.RequestId);
    }

    /// <summary>An actual unsupported official handshake fails before two healthy current-protocol executions.</summary>
    [Test]
    public async Task AcMcp003UnsupportedOfficialProtocolRejectsThenCurrentCallerExecutes()
    {
        using var deadline = McpCallerDeadline.Create();
        var rejected = await Assert.ThrowsAsync<HttpRequestException>(() => McpOfficialClient.ConnectAsync(
            fixture.App, McpCallerProtocol.Node2, fixture.AdminKey,
            McpCallerProtocol.UnsupportedProtocolVersion, deadline.Token));
        await Assert.That(rejected).IsNotNull();
        if (rejected is null)
        { throw new InvalidOperationException(McpCallerProtocol.MissingClient); }
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            fixture.AdminKey, deadline.Token);
        var first = await McpCallerAssertions.SuccessAsync<QueryCapabilityManifest>(await session.Client.InvokeKeyLoadToolAsync(
            McpCallerTools.QueryCapabilities, cancellationToken: deadline.Token));
        var second = await McpCallerAssertions.SuccessAsync<QueryCapabilityManifest>(await session.Client.InvokeKeyLoadToolAsync(
            McpCallerTools.QueryCapabilities, cancellationToken: deadline.Token));
        await Assert.That(first.Value.AstVersion).IsEqualTo(McpCallerProtocol.AstVersion);
        await Assert.That(first.Value.ReadOnly).IsTrue();
        await Assert.That(JsonDefaults.Serialize(first.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(second.Value))).IsTrue();
        await Assert.That(first.RequestId).IsNotEqualTo(second.RequestId);
    }

    /// <summary>Caller cancellation before a read does not fabricate a result or prevent a subsequent genuine request.</summary>
    [Test]
    public async Task AcMcp005CancelledNativeReadDoesNotPreventTheNextRealCall()
    {
        using var deadline = McpCallerDeadline.Create();
        await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            fixture.AdminKey, deadline.Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => session.Client.InvokeKeyLoadToolAsync(
            McpCallerTools.QueryCapabilities, cancellationToken: cancellation.Token).AsTask());
        var next = await McpCallerAssertions.SuccessAsync<QueryCapabilityManifest>(await session.Client.InvokeKeyLoadToolAsync(
            McpCallerTools.QueryCapabilities, cancellationToken: deadline.Token));
        await Assert.That(next.Value.ReadOnly).IsTrue();
    }
}
