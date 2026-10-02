using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClientApi;

namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>AC-AISQL-005/006: SQL lowers to the actual canonical operation decoder without execution.</summary>
internal sealed class SqlOperationCompilerTests
{
    private const string SpacedCall = " \tcall\r\nkeyload_query_capabilities ( @arguments ) ; \n";

    [Test]
    public async Task AcAiSql005SelectAndExplainPreserveExactQ1InputWithoutParsingTwice()
    {
        foreach (var sql in new[] { SqlOperationTestData.Select, SqlOperationTestData.Explain,
                     SqlOperationTestData.InvalidSelect })
        {
            var parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            { [SqlOperationTestData.Status] = JsonSerializer.SerializeToElement(SqlOperationTestData.Open) };
            var request = new SqlOperationRequest(McpCanonicalTestData.Partition, sql, parameters,
                AllowFullScan: true, Cursor: SqlOperationTestData.Cursor);
            var expected = SqlOperationTestData.Find(McpCatalogExpectations.QueryExecute).Decode(
                new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    [McpCatalogProtocol.Request] = JsonSerializer.SerializeToElement(
                        new QueryRequest(request.Partition, sql, parameters, true, request.Cursor), JsonDefaults.Options)
                });
            await SqlOperationTestData.Same(SqlOperationTestData.Compile(request), expected);
        }
    }

    [Test]
    public async Task AcAiSql006AllCanonicalBodyReadsAndCommandsKeepExactKindsIdsAndPayloads()
    {
        foreach (var item in McpCanonicalTestData.Reads().Concat(McpCanonicalTestData.Commands()))
        {
            var arguments = item.Arguments();
            var before = JsonSerializer.Serialize(arguments, JsonDefaults.Options);
            var request = SqlOperationTestData.Call(item.Name, arguments);
            var expected = SqlOperationTestData.Find(item.Name).Decode(arguments);
            await SqlOperationTestData.Same(SqlOperationTestData.Compile(request), expected);
            await Assert.That(JsonSerializer.Serialize(arguments, JsonDefaults.Options)).IsEqualTo(before);
        }
    }

    [Test]
    public async Task AcAiSql005NoBodyOperationsAcceptAnEmptyBoundObjectAndOptionalSemicolon()
    {
        foreach (var name in new[] { McpCatalogExpectations.QueryCapabilities, McpCatalogExpectations.AdminBackup,
                     McpCatalogExpectations.AdminAdmission, McpCatalogExpectations.AdminStatus })
        {
            var request = SqlOperationTestData.Call(name);
            await SqlOperationTestData.Same(SqlOperationTestData.Compile(request),
                SqlOperationTestData.Find(name).Decode(null));
        }
        var spaced = SqlOperationTestData.Request(SpacedCall,
            JsonSerializer.Deserialize<JsonElement>(SqlOperationTestData.EmptyObject));
        await SqlOperationTestData.Same(SqlOperationTestData.Compile(spaced),
            SqlOperationTestData.Find(McpCatalogExpectations.QueryCapabilities).Decode(null));
    }

    [Test]
    public async Task AcAiSql006CompiledPayloadOwnsItsBytesAfterTheBoundDocumentIsDisposed()
    {
        var item = McpCanonicalTestData.Commands()[0];
        McpDecodedOperation compiled;
        using (var document = JsonDocument.Parse(JsonSerializer.Serialize(item.Arguments(), JsonDefaults.Options)))
        {
            compiled = SqlOperationTestData.Compile(SqlOperationTestData.Request(
                SqlOperationTestData.CallPrefix + item.Name + SqlOperationTestData.CallSuffix, document.RootElement));
        }
        await Assert.That(compiled.CommandId).IsEqualTo(item.CommandId);
        await Assert.That(compiled.Payload.Span.SequenceEqual(item.ExpectedPayload.Span)).IsTrue();
    }
}
