using Aspire.Hosting;
using KeyLoad.Client;
using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TwoRf3MembershipReadinessAssertions
{
    internal static async Task VerifyAllNodesAsync(DistributedApplication app, CancellationToken cancellationToken)
    {
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        { await VerifyNodeAsync(app, node, cancellationToken).ConfigureAwait(false); }
    }

    private static async Task VerifyNodeAsync(DistributedApplication app, string node,
        CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(app, node);
        await RequireStatusAsync(http, TwoRf3MembershipProtocol.HealthSilo, 200, cancellationToken).ConfigureAwait(false);
        await RequireStatusAsync(http, TwoRf3MembershipProtocol.HealthMembership, 200, cancellationToken).ConfigureAwait(false);
        await RequireStatusAsync(http, TwoRf3MembershipProtocol.HealthReady, 503, cancellationToken).ConfigureAwait(false);
        var authority = Array.IndexOf(TwoRf3MembershipProtocol.Nodes, node) < TwoRf3MembershipProtocol.MembersPerGroup ? 200 : 503;
        await RequireStatusAsync(http, TwoRf3MembershipProtocol.HealthAuthority, authority, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task VerifyPublicCallsClosedAsync(DistributedApplication app, string node,
        string adminKey, CancellationToken cancellationToken)
    {
        await VerifySdkCallsClosedAsync(app, node, adminKey, cancellationToken).ConfigureAwait(false);
        await VerifyOfficialMcpCallsClosedAsync(app, node, adminKey, cancellationToken).ConfigureAwait(false);
    }

    private static async Task VerifySdkCallsClosedAsync(DistributedApplication app, string node,
        string adminKey, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(app, node);
        var client = new KeyLoadClient(http, adminKey);
        var read = await client.QueryCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        await RequireRejectedAsync(read.IsSuccess, read.Problem?.ErrorCode).ConfigureAwait(false);
        var partition = new PartitionRef("membership-stage1a", "database", "group-closure", node);
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument("membership-probe", "must-not-dispatch", "{\"value\":1}", 0)]);
        var write = await client.CommitAsync(command, cancellationToken).ConfigureAwait(false);
        await RequireRejectedAsync(write.IsSuccess, write.Problem?.ErrorCode).ConfigureAwait(false);
    }

    private static async Task VerifyOfficialMcpCallsClosedAsync(DistributedApplication app, string node,
        string adminKey, CancellationToken cancellationToken)
    {
        await using var mcp = await McpOfficialClient.ConnectAsync(app, node, adminKey, cancellationToken).ConfigureAwait(false);
        var read = await mcp.Client.InvokeKeyLoadToolAsync(McpCallerTools.QueryCapabilities,
            cancellationToken: cancellationToken);
        await McpCallerAssertions.ErrorAsync(read, ErrorCode.OwnershipLost, dispatched: false).ConfigureAwait(false);
        var partition = new PartitionRef("membership-stage1a", "database", "group-closure", node);
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument("membership-probe", "must-not-dispatch", "{\"value\":1}", 0)]);
        var write = await mcp.Client.InvokeKeyLoadToolAsync(McpCallerTools.DocumentsCommit,
            McpOfficialClient.Arguments(command), cancellationToken);
        await McpCallerAssertions.ErrorAsync(write, ErrorCode.OwnershipLost, dispatched: false).ConfigureAwait(false);
    }

    private static async Task RequireStatusAsync(HttpClient http, string path, int expected,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri(http.BaseAddress!, path), cancellationToken).ConfigureAwait(false);
        await Assert.That((int)response.StatusCode).IsEqualTo(expected).Because(TwoRf3MembershipProtocol.MissingState);
    }

    private static async Task RequireRejectedAsync(bool succeeded, string? code)
    {
        await Assert.That(succeeded).IsFalse();
        await Assert.That(code).IsEqualTo(ErrorCode.OwnershipLost.ToString());
    }
}
