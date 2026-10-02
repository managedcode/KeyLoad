using System.Net.Http.Headers;
using System.Net.Http.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>AC-AISQL-007: an insufficient real SQL DATA byte budget leaves direct control progress available.</summary>
[NotInParallel]
internal sealed class SqlRf3AdmissionTests
{
    private const long DataBytes = 536_870_912;
    private const long HeavyReadBytes = 1_073_741_824;
    private const string DispatchRoute = "/v1/admin/dispatch?paused=false";
    private const string CommandIdHeader = "X-KeyLoad-Command-Id";
    private const string TenantPrefix = "sql-admission-";
    private const string Database = "agentdb";
    private const string Domain = "agent";
    private const int Voters = 3;
    private const int NoControlCommands = 0;

    [Test]
    public async Task AcAisql007InsufficientDataBytesRejectSdkAndOfficialSqlBeforeIdClaimWhileDirectControlKeepsRf3Healthy()
    {
        var fixture = new ClusterFixture(new HttpAdmissionLimits
        { MaxReservedBytes = DataBytes, HeavyReadReservedBytes = HeavyReadBytes });
        using var deadline = McpCallerDeadline.Create();
        try
        {
            await fixture.InitializeAsync();
            var partition = new PartitionRef(TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
                Database, Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
            foreach (var node in new[] { McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3 })
            {
                using var http = McpCallerHttp.Create(fixture, node);
                var sdk = new KeyLoadClient(http, fixture.AdminKey);
                await using var mcp = await McpOfficialClient.ConnectAsync(fixture, node, fixture.AdminKey, deadline.Token);
                await VerifySdkRejectionAsync(http, sdk, fixture.AdminKey, partition, deadline.Token);
                await VerifyOfficialRejectionAsync(mcp, partition, deadline.Token);
                await VerifyHealthyAsync(sdk, deadline.Token);
            }
        }
        finally { await fixture.DisposeAsync(); }
    }

    private static async Task VerifySdkRejectionAsync(HttpClient http, KeyLoadClient sdk, string administratorKey,
        PartitionRef partition, CancellationToken cancellationToken)
    {
        var commandId = Guid.NewGuid();
        var sql = SqlRf3Protocol.Call(partition, McpCallerTools.AdminDispatch, true, commandId);
        var rejected = await sdk.ExecuteSqlAsync(sql, cancellationToken);
        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(rejected.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.ResourceExhausted));
        using var control = new HttpRequestMessage(HttpMethod.Post, DispatchRoute);
        control.Headers.Authorization = new AuthenticationHeaderValue(McpCallerProtocol.BearerScheme, administratorKey);
        control.Headers.Add(CommandIdHeader, commandId.ToString());
        using var completed = await http.SendAsync(control, cancellationToken);
        completed.EnsureSuccessStatusCode();
        await Assert.That(await completed.Content.ReadFromJsonAsync<bool>(JsonDefaults.Options, cancellationToken)).IsTrue();
    }

    private static async Task VerifyOfficialRejectionAsync(McpOfficialClient mcp, PartitionRef partition,
        CancellationToken cancellationToken)
    {
        var commandId = Guid.NewGuid();
        var sql = SqlRf3Protocol.Call(partition, McpCallerTools.AdminDispatch, true, commandId);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SqlOperationProtocol.ToolName, sql, cancellationToken),
            ErrorCode.ResourceExhausted, dispatched: false);
        var arguments = McpOfficialClient.Arguments(false);
        arguments.Add(McpCallerProtocol.CommandId, commandId);
        var completed = await McpCallerAssertions.SuccessAsync<bool>(await mcp.Client.CallToolAsync(
            McpCallerTools.AdminDispatch, arguments, cancellationToken: cancellationToken));
        await Assert.That(completed.Value).IsTrue();
    }

    private static async Task VerifyHealthyAsync(KeyLoadClient sdk, CancellationToken cancellationToken)
    {
        var admission = await McpCallerAssertions.SdkSuccessAsync(await sdk.AdmissionStatusAsync(cancellationToken));
        await Assert.That(admission.Http!.Limits.MaxReservedBytes).IsEqualTo(DataBytes);
        await Assert.That(admission.Http.Limits.HeavyReadReservedBytes).IsEqualTo(HeavyReadBytes);
        await Assert.That(admission.Http.Node.ControlCommands).IsEqualTo(NoControlCommands);
        await Assert.That(admission.Http.VerifiedScopes.ControlCommands).IsEqualTo(NoControlCommands);
        var status = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(cancellationToken));
        await Assert.That(status.RoutingReady).IsTrue();
        await Assert.That(status.Voters).IsEqualTo(Voters);
    }
}
