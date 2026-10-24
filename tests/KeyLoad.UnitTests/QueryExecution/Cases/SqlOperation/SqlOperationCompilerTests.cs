using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.UnitTests.ClientApi;

namespace KeyLoad.UnitTests.QueryExecution;

/// <summary>AC-AISQL-005/006: SQL lowers to the actual canonical operation decoder without execution.</summary>
internal sealed class SqlOperationCompilerTests
{
    private const string SpacedCall = " \texec\r\nkeyload_query_capabilities @arguments ; \n";

    [Test]
    public async Task AcAiSql005SelectLowersOnceToTheTypedPlanAndShipsTheAstOperation()
    {
        var limits = Microsoft.Extensions.Options.Options.Create(SqlOperationTestData.Limits);
        var execution = UnitExecutionOptions.QueryExecution();
        foreach (var sql in new[] { SqlOperationTestData.Select, SqlOperationTestData.Top })
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
        await SqlOperationTestData.Reject(new SqlOperationRequest(McpCanonicalTestData.Partition,
            SqlOperationTestData.InvalidSelect, AllowFullScan: true), ErrorCode.UnsupportedCapability);
        await SqlOperationTestData.Reject(SqlOperationTestData.Call(McpCatalogExpectations.QueryCapabilities) with
        {
            Sql = "SELECT TOP () * FROM orders"
        }, ErrorCode.Validation);
    }

    internal static readonly HashSet<string> AdministrationTools = new(StringComparer.Ordinal)
    {
        McpCatalogExpectations.AdminBackup, McpCatalogExpectations.AdminAdmission, McpCatalogExpectations.AdminStatus,
        McpCatalogExpectations.AdminDispatch, McpCatalogExpectations.PrincipalsConfigure, McpCatalogExpectations.CredentialsConfigure
    };

    [Test]
    public async Task AcAiSql006AllCanonicalBodyReadsAndCommandsKeepExactKindsIdsAndPayloads()
    {
        foreach (var item in McpCanonicalTestData.Reads().Concat(McpCanonicalTestData.Commands()))
        {
            var arguments = item.Arguments();
            var before = JsonSerializer.Serialize(arguments, JsonDefaults.Options);
            var request = SqlOperationTestData.Call(item.Name, arguments);
            if (AdministrationTools.Contains(item.Name))
            {
                // Cluster administration and credential tools are reachable through the SDK and MCP only, never through SQL.
                await SqlOperationTestData.Reject(request, ErrorCode.UnsupportedCapability);
                continue;
            }
            var expected = SqlOperationTestData.Find(item.Name).Decode(arguments);
            var compiled = SqlOperationTestData.Compile(request);
            await SqlOperationTestData.Same(compiled, expected);
            _ = await McpPayloadAssertions.AssertFullPublicPayload(item, compiled.Payload);
            await Assert.That(JsonSerializer.Serialize(arguments, JsonDefaults.Options)).IsEqualTo(before);
        }
    }

    [Test]
    public async Task AcAiSql005NoBodyOperationsAcceptAnEmptyBoundObjectAndOptionalSemicolon()
    {
        var request = SqlOperationTestData.Call(McpCatalogExpectations.QueryCapabilities);
        await SqlOperationTestData.Same(SqlOperationTestData.Compile(request),
            SqlOperationTestData.Find(McpCatalogExpectations.QueryCapabilities).Decode(null));
        foreach (var name in new[] { McpCatalogExpectations.AdminBackup, McpCatalogExpectations.AdminAdmission,
                     McpCatalogExpectations.AdminStatus })
        {
            await SqlOperationTestData.Reject(SqlOperationTestData.Call(name), ErrorCode.UnsupportedCapability);
        }
        var spaced = SqlOperationTestData.Request(SpacedCall,
            JsonSerializer.Deserialize<JsonElement>(SqlOperationTestData.EmptyObject));
        await SqlOperationTestData.Same(SqlOperationTestData.Compile(spaced),
            SqlOperationTestData.Find(McpCatalogExpectations.QueryCapabilities).Decode(null));
    }

    [Test]
    public async Task AcJoinUnifiedCallRejectsTheNondefaultQueryDialectAsUnsupported()
    {
        var supported = SqlOperationTestData.Call(McpCatalogExpectations.QueryCapabilities);
        var request = supported with
        {
            QueryDialectVersion = SqlOperationProtocol.DefaultQueryDialectVersion + 1
        };
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => SqlOperationTestData.Compile(request));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await SqlOperationTestData.Same(SqlOperationTestData.Compile(supported),
            SqlOperationTestData.Find(McpCatalogExpectations.QueryCapabilities).Decode(null));
    }

    [Test]
    public async Task AcAiSql006CompiledPayloadOwnsItsBytesAfterTheBoundDocumentIsDisposed()
    {
        var item = McpCanonicalTestData.Commands()[0];
        McpDecodedOperation compiled;
        byte[] copiedWhileDocumentWasAlive;
        using (var document = JsonDocument.Parse(JsonSerializer.Serialize(item.Arguments(), JsonDefaults.Options)))
        {
            compiled = SqlOperationTestData.Compile(SqlOperationTestData.Request(
                SqlOperationTestData.CallPrefix + item.Name + SqlOperationTestData.CallSuffix, document.RootElement));
            copiedWhileDocumentWasAlive = compiled.Payload.ToArray();
        }
        await Assert.That(compiled.CommandId).IsEqualTo(item.CommandId);
        await Assert.That(compiled.Payload.Span.SequenceEqual(copiedWhileDocumentWasAlive)).IsTrue();
        _ = await McpPayloadAssertions.AssertFullPublicPayload(item, compiled.Payload);
    }
}
