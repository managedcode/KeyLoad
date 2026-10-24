using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.QueryExecution;

/// <summary>AC-SQLC-006A: actual typed range results and errors through both Orleans public SQL callers.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SqlClusterBetweenTests(ClusterFixture fixture)
{
    private const string Between = "BETWEEN";
    private const string NotBetween = "NOT BETWEEN";
    private const string Dialect = "TSQL.ScriptDOM180";
    private static ImmutableArray<string> ExpectedPredicates
    {
        get;
    } =
        ["comparison", "AND", "OR", "NOT", "IN", "NOT IN", Between, NotBetween, "IS NULL", "IS NOT NULL",
            "IS DISTINCT FROM", "IS NOT DISTINCT FROM", "AST.IS MISSING"];

    [Test]
    public async Task AcSqlc006AInclusiveAndThreeValuedRangesMatchIndependentRowsOnSdkAndOfficialMcp()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await SqlClusterBetweenScenario.CreateAsync(sdk, deadline.Token);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            fixture.AdminKey, deadline.Token);
        await VerifyAsync(sdk, mcp, scenario,
            scenario.Request(SqlClusterBetweenScenario.Positive, SqlClusterBetweenScenario.Parameters()),
            [SqlClusterBetweenScenario.Lower, SqlClusterBetweenScenario.Middle, SqlClusterBetweenScenario.Upper], deadline.Token);
        await VerifyAsync(sdk, mcp, scenario, scenario.Request(SqlClusterBetweenScenario.Negative),
            [SqlClusterBetweenScenario.Above, SqlClusterBetweenScenario.Below], deadline.Token);
        await VerifyAsync(sdk, mcp, scenario, scenario.Request(SqlClusterBetweenScenario.LowerUnknownNegative),
            [SqlClusterBetweenScenario.Above, SqlClusterBetweenScenario.Middle, SqlClusterBetweenScenario.Upper], deadline.Token);
        await VerifyAsync(sdk, mcp, scenario, scenario.Request(SqlClusterBetweenScenario.UpperUnknownNegative),
            [SqlClusterBetweenScenario.Below], deadline.Token);
        await VerifyAsync(sdk, mcp, scenario, scenario.Request(SqlClusterBetweenScenario.LowerUnknown), [], deadline.Token);
        await VerifyAsync(sdk, mcp, scenario, scenario.Request(SqlClusterBetweenScenario.UpperUnknown), [], deadline.Token);
        var capabilities = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryCapabilitiesAsync(deadline.Token));
        await Assert.That(capabilities.Predicates).IsEquivalentTo(ExpectedPredicates, CollectionOrdering.Matching);
        await Assert.That(capabilities.SqlDialect).IsEqualTo(Dialect);
        await Assert.That(capabilities.ReadOnly).IsTrue();
        await Assert.That(capabilities.FullScanRequiresOptIn).IsTrue();
        var official = await SqlClusterProtocol.McpAsync<QueryCapabilityManifest>(mcp,
            SqlClusterProtocol.NoBodyCall(scenario.Partition, McpCallerTools.QueryCapabilities), deadline.Token);
        await SqlClusterProtocol.EqualAsync(capabilities, official);
    }

    [Test]
    public async Task AcSqlc006AInvalidRangesKeepSafeErrorsAndTheNextRealQuerySucceeds()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await SqlClusterBetweenScenario.CreateAsync(sdk, deadline.Token);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            fixture.AdminKey, deadline.Token);
        foreach (var predicate in new[] { SqlClusterBetweenScenario.Malformed, SqlClusterBetweenScenario.QuotedOperator })
        {
            await RejectAsync(sdk, mcp, scenario.Request(predicate), ErrorCode.Validation, false, deadline.Token);
        }
        foreach (var predicate in new[] { SqlClusterBetweenScenario.EagerMismatch, SqlClusterBetweenScenario.MissingParameter })
        {
            await RejectAsync(sdk, mcp, scenario.Request(predicate), ErrorCode.Validation, true, deadline.Token);
        }
        await RejectAsync(sdk, mcp, scenario.Request(SqlClusterBetweenScenario.NonScalarParameter,
            SqlClusterBetweenScenario.Parameters(nonScalar: true)), ErrorCode.UnsupportedCapability, true, deadline.Token);
        await VerifyAsync(sdk, mcp, scenario,
            scenario.Request(SqlClusterBetweenScenario.Positive, SqlClusterBetweenScenario.Parameters()),
            [SqlClusterBetweenScenario.Lower, SqlClusterBetweenScenario.Middle, SqlClusterBetweenScenario.Upper], deadline.Token);
    }

    private static async Task VerifyAsync(KeyLoadClient sdk, McpOfficialClient mcp, SqlClusterBetweenScenario scenario,
        SqlOperationRequest request, string[] expected, CancellationToken cancellationToken)
    {
        var page = await SqlClusterProtocol.SdkAsync<QueryPage>(sdk, request, cancellationToken);
        var official = await SqlClusterProtocol.McpAsync<QueryPage>(mcp, request, cancellationToken);
        foreach (var actual in new[] { page, official })
        {
            await Assert.That(actual.Rows.Select(row => row.EntityId)).IsEquivalentTo(expected, CollectionOrdering.Matching);
            await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(scenario.CommittedPosition);
            await Assert.That(actual.AccessPath).IsEqualTo(SqlClusterBetweenScenario.IndexPath);
            await Assert.That(actual.Cursor).IsNull();
        }
        await SqlClusterProtocol.EqualAsync(page.Rows, official.Rows);
    }

    // A syntax refusal happens while the gateway compiles the SQL envelope, before an operation is dispatched.
    private static async Task RejectAsync(KeyLoadClient sdk, McpOfficialClient mcp, SqlOperationRequest request,
        ErrorCode error, bool dispatched, CancellationToken cancellationToken)
    {
        var result = await sdk.ExecuteSqlAsync(request, cancellationToken);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(error.ToString());
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SqlOperationProtocol.ToolName, request, cancellationToken),
            error, dispatched);
    }
}
