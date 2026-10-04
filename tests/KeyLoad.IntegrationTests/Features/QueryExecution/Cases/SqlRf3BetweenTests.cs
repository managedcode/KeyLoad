using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>AC-SQLC-006A: actual typed range results and errors through both RF3 public SQL callers.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SqlRf3BetweenTests(ClusterFixture fixture)
{
    private const string Between = "BETWEEN";
    private const string NotBetween = "NOT BETWEEN";
    private const string Dialect = "Q1";
    private static ImmutableArray<string> ExpectedPredicates { get; } =
        ["comparison", "AND", "OR", "NOT", "IN", Between, NotBetween, "IS NULL", "IS MISSING"];

    [Test]
    public async Task AcSqlc006AInclusiveAndThreeValuedRangesMatchIndependentRowsOnSdkAndOfficialMcp()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey);
        var scenario = await SqlRf3BetweenScenario.CreateAsync(sdk, deadline.Token);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            fixture.AdminKey, deadline.Token);
        await VerifyAsync(sdk, mcp, scenario,
            scenario.Request(SqlRf3BetweenScenario.Positive, SqlRf3BetweenScenario.Parameters()),
            [SqlRf3BetweenScenario.Lower, SqlRf3BetweenScenario.Middle, SqlRf3BetweenScenario.Upper], deadline.Token);
        await VerifyAsync(sdk, mcp, scenario, scenario.Request(SqlRf3BetweenScenario.Negative),
            [SqlRf3BetweenScenario.Above, SqlRf3BetweenScenario.Below], deadline.Token);
        await VerifyAsync(sdk, mcp, scenario, scenario.Request(SqlRf3BetweenScenario.LowerUnknownNegative),
            [SqlRf3BetweenScenario.Above, SqlRf3BetweenScenario.Middle, SqlRf3BetweenScenario.Upper], deadline.Token);
        await VerifyAsync(sdk, mcp, scenario, scenario.Request(SqlRf3BetweenScenario.UpperUnknownNegative),
            [SqlRf3BetweenScenario.Below], deadline.Token);
        await VerifyAsync(sdk, mcp, scenario, scenario.Request(SqlRf3BetweenScenario.LowerUnknown), [], deadline.Token);
        await VerifyAsync(sdk, mcp, scenario, scenario.Request(SqlRf3BetweenScenario.UpperUnknown), [], deadline.Token);
        var capabilities = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryCapabilitiesAsync(deadline.Token));
        await Assert.That(capabilities.Predicates).IsEquivalentTo(ExpectedPredicates, CollectionOrdering.Matching);
        await Assert.That(capabilities.SqlDialect).IsEqualTo(Dialect);
        await Assert.That(capabilities.ReadOnly).IsTrue();
        await Assert.That(capabilities.FullScanRequiresOptIn).IsTrue();
        var official = await SqlRf3Protocol.McpAsync<QueryCapabilityManifest>(mcp,
            SqlRf3Protocol.NoBodyCall(scenario.Partition, McpCallerTools.QueryCapabilities), deadline.Token);
        await SqlRf3Protocol.EqualAsync(capabilities, official);
    }

    [Test]
    public async Task AcSqlc006AInvalidRangesKeepSafeErrorsAndTheNextRealQuerySucceeds()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey);
        var scenario = await SqlRf3BetweenScenario.CreateAsync(sdk, deadline.Token);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            fixture.AdminKey, deadline.Token);
        foreach (var predicate in new[] { SqlRf3BetweenScenario.Malformed, SqlRf3BetweenScenario.QuotedOperator,
            SqlRf3BetweenScenario.EagerMismatch, SqlRf3BetweenScenario.MissingParameter })
        {
            await RejectAsync(sdk, mcp, scenario.Request(predicate), ErrorCode.Validation, deadline.Token);
        }
        await RejectAsync(sdk, mcp, scenario.Request(SqlRf3BetweenScenario.NonScalarParameter,
            SqlRf3BetweenScenario.Parameters(nonScalar: true)), ErrorCode.UnsupportedCapability, deadline.Token);
        await VerifyAsync(sdk, mcp, scenario,
            scenario.Request(SqlRf3BetweenScenario.Positive, SqlRf3BetweenScenario.Parameters()),
            [SqlRf3BetweenScenario.Lower, SqlRf3BetweenScenario.Middle, SqlRf3BetweenScenario.Upper], deadline.Token);
    }

    private static async Task VerifyAsync(KeyLoadClient sdk, McpOfficialClient mcp, SqlRf3BetweenScenario scenario,
        SqlOperationRequest request, string[] expected, CancellationToken cancellationToken)
    {
        var page = await SqlRf3Protocol.SdkAsync<QueryPage>(sdk, request, cancellationToken);
        var official = await SqlRf3Protocol.McpAsync<QueryPage>(mcp, request, cancellationToken);
        foreach (var actual in new[] { page, official })
        {
            await Assert.That(actual.Rows.Select(row => row.EntityId)).IsEquivalentTo(expected, CollectionOrdering.Matching);
            await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(scenario.CommittedPosition);
            await Assert.That(actual.AccessPath).IsEqualTo(SqlRf3BetweenScenario.IndexPath);
            await Assert.That(actual.Cursor).IsNull();
        }
        await SqlRf3Protocol.EqualAsync(page.Rows, official.Rows);
    }

    private static async Task RejectAsync(KeyLoadClient sdk, McpOfficialClient mcp, SqlOperationRequest request,
        ErrorCode error, CancellationToken cancellationToken)
    {
        var result = await sdk.ExecuteSqlAsync(request, cancellationToken);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(error.ToString());
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SqlOperationProtocol.ToolName, request, cancellationToken),
            error, dispatched: true);
    }
}
