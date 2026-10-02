using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClientApi;

namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>AC-AISQL-005/006: malformed input never reaches a database operation.</summary>
internal sealed class SqlOperationRejectionTests
{
    private const string QuotedCall = "CALL \"keyload_query_capabilities\"(@arguments)";
    private const string LiteralCall = "CALL keyload_query_capabilities('{}')";
    private const string MissingParenthesis = "CALL keyload_query_capabilities(@arguments";
    private const string MultipleStatements = "CALL keyload_query_capabilities(@arguments); SELECT * FROM orders";
    private const string MultipleArguments = "CALL keyload_query_capabilities(@arguments,@extra)";
    private const string CommentCall = "CALL keyload_query_capabilities(@arguments) -- comment";
    private const string QuotedSelect = "\"SELECT\" * FROM orders";
    private const string MutationSql = "UPDATE internal_queue SET state = 'Completed'";
    private const string EmptySql = " \t\r\n";
    private const string DuplicateArguments = "{\"request\":false,\"request\":true,\"commandId\":\"ee5cf37e-0e81-4a94-8725-021e30f7b721\"}";
    private const string NestedDuplicateArguments = "{\"request\":{\"reference\":{\"collection\":\"one\",\"collection\":\"two\"}}}";
    private const string InvalidIdentity = "not-a-command-id";

    [Test]
    public async Task AcAiSql005RejectsUnknownVersionsTargetsCaseAndRecursiveAdapter()
    {
        var request = SqlOperationTestData.Call(McpCatalogExpectations.QueryCapabilities);
        await SqlOperationTestData.Reject(request with { Version = SqlOperationProtocol.Version + 1 },
            ErrorCode.UnsupportedCapability);
        foreach (var sql in new[] { SqlOperationTestData.UnknownCall, SqlOperationTestData.WrongCaseCall,
                     SqlOperationTestData.RecursiveCall, QuotedSelect, MutationSql })
        {
            await SqlOperationTestData.Reject(request with { Sql = sql }, ErrorCode.UnsupportedCapability);
        }
    }

    [Test]
    public async Task AcAiSql005RejectsMalformedCallQuotedNamesLiteralArgumentsAndTrailingStatements()
    {
        var request = SqlOperationTestData.Call(McpCatalogExpectations.QueryCapabilities);
        foreach (var sql in new[] { QuotedCall, LiteralCall, MissingParenthesis, MultipleStatements,
                     MultipleArguments, CommentCall, EmptySql })
        {
            await SqlOperationTestData.Reject(request with { Sql = sql }, ErrorCode.Validation);
        }
        await SqlOperationTestData.Reject(request with { Cursor = SqlOperationTestData.Cursor }, ErrorCode.Validation);
        await SqlOperationTestData.Reject(request with { AllowFullScan = true }, ErrorCode.Validation);
    }

    [Test]
    public async Task AcAiSql005CallRequiresExactlyOnePresentObjectParameter()
    {
        var request = SqlOperationTestData.Call(McpCatalogExpectations.QueryCapabilities);
        await SqlOperationTestData.Reject(request with { Parameters = null }, ErrorCode.Validation);
        await SqlOperationTestData.Reject(request with { Parameters = [] }, ErrorCode.Validation);
        await SqlOperationTestData.Reject(request with
        {
            Parameters = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            { [SqlOperationTestData.ExtraParameter] = request.Parameters![SqlOperationTestData.Parameter] }
        }, ErrorCode.Validation);
        var extra = new Dictionary<string, JsonElement>(request.Parameters!, StringComparer.Ordinal)
        { [SqlOperationTestData.ExtraParameter] = JsonSerializer.SerializeToElement(SqlOperationTestData.PrivateMarker) };
        await SqlOperationTestData.Reject(request with { Parameters = extra }, ErrorCode.Validation);
        foreach (var json in new[] { SqlOperationTestData.Null, SqlOperationTestData.Array })
        {
            await SqlOperationTestData.Reject(SqlOperationTestData.Request(request.Sql,
                JsonSerializer.Deserialize<JsonElement>(json)), ErrorCode.Validation);
        }
    }

    [Test]
    public async Task AcAiSql006CanonicalDecoderRejectsNullWrongCaseExtraKeysAndMissingStableIdentity()
    {
        var read = McpCanonicalTestData.Reads()[0];
        var arguments = read.Arguments();
        arguments[McpCatalogProtocol.Request] = JsonSerializer.Deserialize<JsonElement>(SqlOperationTestData.Null);
        await SqlOperationTestData.Reject(SqlOperationTestData.Call(read.Name, arguments), ErrorCode.Validation);
        arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        { [SqlOperationTestData.WrongCaseKey] = read.Request };
        await SqlOperationTestData.Reject(SqlOperationTestData.Call(read.Name, arguments), ErrorCode.Validation);
        arguments = read.Arguments();
        arguments[SqlOperationTestData.ExtraParameter] = JsonSerializer.SerializeToElement(SqlOperationTestData.PrivateMarker);
        await SqlOperationTestData.Reject(SqlOperationTestData.Call(read.Name, arguments), ErrorCode.Validation);
        var header = McpCanonicalTestData.Commands().First(item => item.IdMember is null);
        arguments = header.Arguments();
        arguments.Remove(McpCatalogProtocol.CommandId);
        await SqlOperationTestData.Reject(SqlOperationTestData.Call(header.Name, arguments), ErrorCode.Validation);
        arguments[McpCatalogProtocol.CommandId] = JsonSerializer.SerializeToElement(InvalidIdentity);
        await SqlOperationTestData.Reject(SqlOperationTestData.Call(header.Name, arguments), ErrorCode.Validation);
    }

    [Test]
    public async Task AcAiSql005DuplicateArgumentMembersCannotChangeTheCanonicalMeaning()
    {
        var duplicate = SqlOperationTestData.Request(SqlOperationTestData.CallPrefix +
            McpCatalogExpectations.AdminDispatch + SqlOperationTestData.CallSuffix,
            JsonSerializer.Deserialize<JsonElement>(DuplicateArguments));
        await SqlOperationTestData.Reject(duplicate, ErrorCode.Validation);
        duplicate = SqlOperationTestData.Request(SqlOperationTestData.CallPrefix +
            McpCatalogExpectations.DocumentsGet + SqlOperationTestData.CallSuffix,
            JsonSerializer.Deserialize<JsonElement>(NestedDuplicateArguments));
        await SqlOperationTestData.Reject(duplicate, ErrorCode.Validation);
    }
}
