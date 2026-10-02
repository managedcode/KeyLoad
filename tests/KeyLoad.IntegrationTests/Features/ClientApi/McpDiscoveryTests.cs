using KeyLoad.Query;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>AC-MCP-001/003/007: genuine official discovery exposes the complete frozen RF3 capability contract.</summary>
/// <param name="fixture">The initialized actual Docker/Aspire RF3 application.</param>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class McpDiscoveryTests(ClusterFixture fixture)
{
    /// <summary>Raw native cursors and the SDK's aggregate API both discover every declared schema and hint.</summary>
    [Test]
    public async Task AcMcp001OfficialClientDiscoversAllPagesWithCanonicalSchemasAndHints()
    {
        using var deadline = McpCallerDeadline.Create();
        await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node1,
            fixture.AdminKey, deadline.Token);
        var discovered = new HashSet<string>(StringComparer.Ordinal);
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            var page = await session.Client.ListToolsAsync(new ListToolsRequestParams { Cursor = cursor }, deadline.Token);
            await Assert.That(page.Tools.Count).IsGreaterThan(0);
            await Assert.That(discovered.Count + page.Tools.Count).IsLessThanOrEqualTo(McpCallerProtocol.ToolCount);
            foreach (var tool in page.Tools)
            {
                await Assert.That(discovered.Add(tool.Name)).IsTrue();
                await McpDiscoveryAssertions.VerifyAsync(tool);
            }
            cursor = page.NextCursor;
            if (cursor is not null)
            { await Assert.That(cursors.Add(cursor)).IsTrue(); }
        } while (cursor is not null);

        await McpDiscoveryAssertions.VerifyInventoryAsync(discovered);
        var aggregate = await session.Client.ListToolsAsync(cancellationToken: deadline.Token);
        await Assert.That(aggregate.Count).IsEqualTo(McpCallerProtocol.ToolCount);
        await McpDiscoveryAssertions.VerifyInventoryAsync(aggregate.Select(tool => tool.Name));
    }

    /// <summary>No-body official calls decode the canonical capability manifest and receive separate execution identities.</summary>
    [Test]
    public async Task AcMcp007OfficialClientReceivesCanonicalCapabilitiesAndFreshExecutionIds()
    {
        using var deadline = McpCallerDeadline.Create();
        await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            fixture.AdminKey, deadline.Token);
        var first = await McpCallerAssertions.SuccessAsync<QueryCapabilityManifest>(await session.Client.CallToolAsync(
            McpCallerTools.QueryCapabilities, cancellationToken: deadline.Token));
        var second = await McpCallerAssertions.SuccessAsync<QueryCapabilityManifest>(await session.Client.CallToolAsync(
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
        await Assert.ThrowsAsync<OperationCanceledException>(() => session.Client.CallToolAsync(
            McpCallerTools.QueryCapabilities, cancellationToken: cancellation.Token).AsTask());
        var next = await McpCallerAssertions.SuccessAsync<QueryCapabilityManifest>(await session.Client.CallToolAsync(
            McpCallerTools.QueryCapabilities, cancellationToken: deadline.Token));
        await Assert.That(next.Value.ReadOnly).IsTrue();
    }
}
