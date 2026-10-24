using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.UnitTests.ClientApi;

namespace KeyLoad.UnitTests.QueryExecution;

/// <summary>AC-SQLC-002/003: comments preserve actual catalog payloads and caller identities.</summary>
internal sealed class SqlOperationCommentCompilerTests
{
    [Test]
    public async Task AcSqlc003EveryCanonicalReadAndCommandPreservesKindsIdentityAndPayload()
    {
        foreach (var item in McpCanonicalTestData.Reads().Concat(McpCanonicalTestData.Commands()))
        {
            var arguments = item.Arguments();
            var before = JsonSerializer.Serialize(arguments, JsonDefaults.Options);
            var plain = SqlOperationTestData.Call(item.Name, arguments);
            var commented = plain with
            {
                Sql = SqlOperationCommentTestData.Call(item.Name)
            };
            if (SqlOperationCompilerTests.AdministrationTools.Contains(item.Name))
            {
                await SqlOperationTestData.Reject(commented, ErrorCode.UnsupportedCapability);
                continue;
            }
            var expected = SqlOperationTestData.Find(item.Name).Decode(arguments);
            var actual = SqlOperationTestData.Compile(commented);
            await SqlOperationTestData.Same(actual, expected);
            await Assert.That(actual.CommandId).IsEqualTo(item.CommandId);
            _ = await McpPayloadAssertions.AssertFullPublicPayload(item, actual.Payload);
            await Assert.That(JsonSerializer.Serialize(arguments, JsonDefaults.Options)).IsEqualTo(before);
        }
    }

    [Test]
    public async Task AcSqlc002EofLineCrLfAndTrailingNestedCommentsAreAccepted()
    {
        foreach (var sql in new[]
                 {
                     SqlOperationCommentTestData.PlainCall + SqlOperationCommentTestData.EndOfFileLine,
                     SqlOperationCommentTestData.PlainCall + SqlOperationCommentTestData.Semicolon
                         + SqlOperationCommentTestData.EndOfFileLine,
                     SqlOperationCommentTestData.CrLfCall,
                     SqlOperationCommentTestData.Call(McpCatalogExpectations.QueryCapabilities)
                 })
        {
            await SqlOperationTestData.Same(SqlOperationTestData.Compile(SqlOperationCommentTestData.Request(sql)),
                SqlOperationTestData.Find(McpCatalogExpectations.QueryCapabilities).Decode(null));
        }
    }

    [Test]
    public async Task AcSqlc002DelimiterPairsAcrossChunkEdgesRemainWhole()
    {
        foreach (var prefix in SqlOperationCommentTestData.BoundaryPrefixes())
        {
            var request = SqlOperationCommentTestData.Request(prefix + SqlOperationCommentTestData.PlainCall);
            await SqlOperationTestData.Same(SqlOperationTestData.Compile(request),
                SqlOperationTestData.Find(McpCatalogExpectations.QueryCapabilities).Decode(null));
        }
    }

    [Test]
    public async Task AcSqlc003CommentedSelectLowersToTheSameTypedPlanAsThePlainText()
    {
        var limits = Microsoft.Extensions.Options.Options.Create(SqlOperationTestData.Limits);
        var execution = UnitExecutionOptions.QueryExecution();
        foreach (var sql in new[] { SqlOperationCommentTestData.Select, SqlOperationCommentTestData.Top })
        {
            var parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                [SqlOperationTestData.Status] = JsonSerializer.SerializeToElement(SqlOperationTestData.Open)
            };
            var request = new SqlOperationRequest(McpCanonicalTestData.Partition, sql, parameters,
                AllowFullScan: true, Cursor: SqlOperationTestData.Cursor);
            var query = new QueryRequest(request.Partition, sql, parameters, true, request.Cursor);
            var budget = new global::KeyLoad.Core.ReadExecutionBudget(limits);
            var plan = global::KeyLoad.Query.QueryExecution.ScriptDomQueryAdapter.Lower(
                global::KeyLoad.QueryExecution.ScriptDomSyntax.Parse(sql, limits, budget.Check, CancellationToken.None),
                query, limits, execution, budget, null, CancellationToken.None);
            var expected = SqlOperationTestData.Find(McpCatalogExpectations.QueryAst).Decode(
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    [McpCatalogProtocol.Request] = JsonSerializer.SerializeToElement(plan, JsonDefaults.Options)
                });
            await SqlOperationTestData.Same(SqlOperationTestData.Compile(request), expected);
        }
    }

    [Test]
    public async Task AcSqlc003BoundDocumentCommentTextRemainsExactUserContent()
    {
        var command = new CommandRequest(McpCanonicalTestData.StableId, McpCanonicalTestData.Partition,
            [new PutDocument(McpCanonicalTestData.Resource, McpCanonicalTestData.Entity, SqlOperationCommentTestData.Document)]);
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [McpCatalogProtocol.Request] = JsonSerializer.SerializeToElement(command, JsonDefaults.Options)
        };
        var request = SqlOperationTestData.Call(McpCatalogExpectations.DocumentsCommit, arguments)
            with
        {
            Sql = SqlOperationCommentTestData.Call(McpCatalogExpectations.DocumentsCommit)
        };
        var actual = SqlOperationTestData.Compile(request);
        await Assert.That(actual.CommandId).IsEqualTo(McpCanonicalTestData.StableId);
        await SqlOperationTestData.Same(actual, SqlOperationTestData.Find(McpCatalogExpectations.DocumentsCommit).Decode(arguments));
        var decoded = McpArgumentDecoder.Request<CommandRequest>(arguments, false);
        await Assert.That(((PutDocument)decoded.Mutations.Single()).Json).IsEqualTo(SqlOperationCommentTestData.Document);
    }
}
