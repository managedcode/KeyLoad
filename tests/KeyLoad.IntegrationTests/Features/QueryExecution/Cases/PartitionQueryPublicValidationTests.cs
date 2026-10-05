using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>AC-PQUERY-001 and AC-PQUERY-005 reject bounded-shape, byte-limit and caller-cancellation cases.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class PartitionQueryPublicValidationTests(ClusterFixture fixture)
{
    private const int InvalidVersion = 2;
    private const int OversizedValueChars = 70_000;

    [Test]
    public async Task AcPquery001InvalidVersionAndLeafOverflowAreValidationWithHealthyFollowup()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await PartitionQueryRf3Scenario.CreateAsync(fixture, deadline.Token).ConfigureAwait(false);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            fixture.AdminKey, deadline.Token).ConfigureAwait(false);
        await AssertFailureAsync(sdk, mcp, scenario.Request() with { Version = InvalidVersion },
            ErrorCode.Validation, deadline.Token).ConfigureAwait(false);
        await AssertFailureAsync(sdk, mcp, scenario.Request() with { AstVersion = InvalidVersion },
            ErrorCode.UnsupportedCapability, deadline.Token).ConfigureAwait(false);
        await AssertFailureAsync(sdk, mcp, scenario.Request([]), ErrorCode.Validation, deadline.Token)
            .ConfigureAwait(false);
        await AssertFailureAsync(sdk, mcp,
            scenario.Request([scenario.Partitions[0], scenario.Partitions[0]]), ErrorCode.Validation, deadline.Token)
            .ConfigureAwait(false);
        var overflow = scenario.Request(CreateTooManyPartitions(scenario.Partitions));
        await AssertFailureAsync(sdk, mcp, overflow, ErrorCode.Validation, deadline.Token).ConfigureAwait(false);
        var resultLimit = scenario.Request(query: scenario.Query with
        { Limit = PartitionQueryRf3Protocol.MaxResults + 1 });
        await AssertFailureAsync(sdk, mcp, resultLimit, ErrorCode.BudgetExceeded, deadline.Token)
            .ConfigureAwait(false);
        var valid = scenario.Request([scenario.Partitions[0]]);
        var page = await McpCallerAssertions.SdkSuccessAsync(await sdk.PartitionQueryAsync(valid, deadline.Token)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await PartitionQueryRf3Assertions.AssertOracleAsync(page!, scenario, valid.Partitions).ConfigureAwait(false);
        await AssertHealthyMcpAsync(mcp, scenario, deadline.Token).ConfigureAwait(false);
    }

    [Test]
    public async Task AcPquery005OversizedRequestAndCancelledSdkAndMcpCallsHaveHealthyFollowups()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await PartitionQueryRf3Scenario.CreateAsync(fixture, deadline.Token).ConfigureAwait(false);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey);
        var oversized = CreateOversizedRequest(scenario);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            fixture.AdminKey, deadline.Token).ConfigureAwait(false);
        await AssertFailureAsync(sdk, mcp, oversized, ErrorCode.BudgetExceeded, deadline.Token)
            .ConfigureAwait(false);
        await AssertHealthySdkAsync(sdk, scenario, deadline.Token).ConfigureAwait(false);

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        await cancellation.CancelAsync().ConfigureAwait(false);
        var cancelled = await sdk.PartitionQueryAsync(scenario.Request(), cancellation.Token).ConfigureAwait(false);
        await Assert.That(cancelled.IsSuccess).IsFalse();
        await Assert.That(cancelled.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Cancelled));
        using var mcpCancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        await mcpCancellation.CancelAsync().ConfigureAwait(false);
        await Assert.ThrowsAsync<OperationCanceledException>(() => mcp.CallAsync(PartitionQueryRf3Protocol.ToolName,
            scenario.Request(), mcpCancellation.Token));
        await AssertHealthySdkAsync(sdk, scenario, deadline.Token).ConfigureAwait(false);
        await AssertHealthyMcpAsync(mcp, scenario, deadline.Token).ConfigureAwait(false);
    }

    private static async Task AssertFailureAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        PartitionQueryRequestV1 request, ErrorCode expected, CancellationToken cancellationToken)
    {
        var result = await sdk.PartitionQueryAsync(request, cancellationToken).ConfigureAwait(false);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(expected.ToString());
        var reply = await mcp.CallAsync(PartitionQueryRf3Protocol.ToolName, request, cancellationToken)
            .ConfigureAwait(false);
        await McpCallerAssertions.ErrorAsync(reply, expected, dispatched: true).ConfigureAwait(false);
    }

    private static ImmutableArray<PartitionRef> CreateTooManyPartitions(ImmutableArray<PartitionRef> known)
        => known.AddRange(Enumerable.Range(0, PartitionQueryRf3Protocol.MaxPartitions - known.Length + 1)
            .Select(index => known[0] with { PartitionKey = "overflow-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) }));

    private static PartitionQueryRequestV1 CreateOversizedRequest(PartitionQueryRf3Scenario scenario)
    {
        var largeText = new string('x', OversizedValueChars);
        var parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        { ["value"] = JsonSerializer.SerializeToElement(largeText, JsonDefaults.Options) };
        var query = scenario.Query with
        {
            Filter = new Comparison(new FieldOperand(PartitionQueryRf3Protocol.ValueField), "=",
                new ParameterOperand("value"))
        };
        return scenario.Request([scenario.Partitions[0]], query) with { Parameters = parameters };
    }

    private static async Task AssertHealthySdkAsync(KeyLoadClient sdk, PartitionQueryRf3Scenario scenario,
        CancellationToken cancellationToken)
    {
        var request = scenario.Request([scenario.Partitions[0]]);
        var page = await McpCallerAssertions.SdkSuccessAsync(await sdk.PartitionQueryAsync(request, cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await PartitionQueryRf3Assertions.AssertOracleAsync(page!, scenario, request.Partitions).ConfigureAwait(false);
    }

    private static async Task AssertHealthyMcpAsync(McpOfficialClient mcp, PartitionQueryRf3Scenario scenario,
        CancellationToken cancellationToken)
    {
        var request = scenario.Request([scenario.Partitions[1]]);
        var receipt = await McpCallerAssertions.SuccessAsync<PartitionQueryPageV1>(await mcp.CallAsync(
            PartitionQueryRf3Protocol.ToolName, request, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await PartitionQueryRf3Assertions.AssertOracleAsync(receipt.Value, scenario, request.Partitions)
            .ConfigureAwait(false);
    }
}
