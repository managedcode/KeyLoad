using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClientApi;

namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>AC-SQLC-002/003: trivia cannot splice tokens, hide statements or relax bound arguments.</summary>
internal sealed class SqlOperationCommentRejectionTests
{
    private const string UnclosedLeading = "/* private-input-canary CALL keyload_query_capabilities(@arguments)";
    private const string UnclosedRoot = "CALL /* private-input-canary";
    private const string UnclosedArgument = "CALL keyload_query_capabilities(@ /* private-input-canary";
    private const string UnclosedTrailing = "CALL keyload_query_capabilities(@arguments); /* private-input-canary";
    private const string SplitRoot = "CA/* private-input-canary */LL keyload_query_capabilities(@arguments)";
    private const string SplitName = "CALL keyload_query_/* private-input-canary */capabilities(@arguments)";
    private const string SplitParameter = "CALL keyload_query_capabilities(@arg/* private-input-canary */uments)";
    private const string SplitLineName = "CALL keyload_-- private-input-canary\r\nquery_capabilities(@arguments)";
    private const string SplitDelimiter = "CALL / /* private-input-canary */ * keyload_query_capabilities(@arguments)";
    private const string SecondStatement = "CALL keyload_query_capabilities(@arguments); /* hidden */ SELECT * FROM orders";
    private const string SecondCall = "CALL keyload_query_capabilities(@arguments) -- hidden\r\nCALL keyload_admin_status(@arguments)";
    private const string ExtraTerminator = "CALL keyload_query_capabilities(@arguments); /* hidden */ ;";

    [Test]
    public async Task AcSqlc002UnclosedBlocksAtEveryCallBoundaryReturnSafeValidation()
    {
        foreach (var sql in new[] { UnclosedLeading, UnclosedRoot, UnclosedArgument, UnclosedTrailing })
        {
            await SqlOperationTestData.Reject(SqlOperationCommentTestData.Request(sql), ErrorCode.Validation);
        }
    }

    [Test]
    public async Task AcSqlc002ConfiguredCommentDepthAcceptsEqualityAndRejectsOneMore()
    {
        var limits = SqlOperationTestData.Limits with { MaxQueryDepth = SqlOperationCommentTestData.ShallowDepth };
        var accepted = SqlOperationCommentTestData.Request(
            SqlOperationCommentTestData.Nested(SqlOperationCommentTestData.ShallowDepth) + SqlOperationCommentTestData.PlainCall);
        await SqlOperationTestData.Same(SqlOperationTestData.Compile(accepted, limits),
            SqlOperationTestData.Find(McpCatalogExpectations.QueryCapabilities).Decode(null));
        var rejected = accepted with
        {
            Sql = SqlOperationCommentTestData.Nested(SqlOperationCommentTestData.ExcessDepth) + SqlOperationCommentTestData.PlainCall
        };
        await SqlOperationTestData.Reject(rejected, ErrorCode.BudgetExceeded, limits);
    }

    [Test]
    public async Task AcSqlc002CommentsCannotConcatenateIdentifiersOrDelimiterCharacters()
    {
        await SqlOperationTestData.Reject(SqlOperationCommentTestData.Request(SplitRoot), ErrorCode.UnsupportedCapability);
        foreach (var sql in new[] { SplitName, SplitParameter, SplitLineName, SplitDelimiter })
        {
            await SqlOperationTestData.Reject(SqlOperationCommentTestData.Request(sql), ErrorCode.Validation);
        }
    }

    [Test]
    public async Task AcSqlc002CommentsCannotHideAnotherStatementOrTerminator()
    {
        foreach (var sql in new[] { SecondStatement, SecondCall, ExtraTerminator })
        {
            await SqlOperationTestData.Reject(SqlOperationCommentTestData.Request(sql), ErrorCode.Validation);
        }
    }

    [Test]
    public async Task AcSqlc003CommentedCatalogTargetsPreserveCaseAndRecursionRejection()
    {
        foreach (var sql in new[] { SqlOperationTestData.UnknownCall, SqlOperationTestData.WrongCaseCall,
                     SqlOperationTestData.RecursiveCall })
        {
            await SqlOperationTestData.Reject(SqlOperationCommentTestData.Request(SqlOperationCommentTestData.Block + sql),
                ErrorCode.UnsupportedCapability);
        }
    }

    [Test]
    public async Task AcSqlc003CommentsDoNotRelaxOrdinalBindingOrDuplicateArgumentValidation()
    {
        var request = SqlOperationCommentTestData.Request(SqlOperationCommentTestData.Call(McpCatalogExpectations.QueryCapabilities));
        var parameters = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        { [SqlOperationCommentTestData.WrongCaseParameter] = request.Parameters![SqlOperationTestData.Parameter] };
        await SqlOperationTestData.Reject(request with { Parameters = parameters }, ErrorCode.Validation);
        var duplicate = SqlOperationTestData.Request(SqlOperationCommentTestData.Call(McpCatalogExpectations.AdminDispatch),
            JsonSerializer.Deserialize<JsonElement>(SqlOperationCommentTestData.DuplicateArguments));
        await SqlOperationTestData.Reject(duplicate, ErrorCode.Validation);
        var header = McpCanonicalTestData.Commands().First(item => item.IdMember is null);
        var arguments = header.Arguments();
        arguments.Remove(McpCatalogProtocol.CommandId);
        var missingId = SqlOperationTestData.Call(header.Name, arguments) with { Sql = SqlOperationCommentTestData.Call(header.Name) };
        await SqlOperationTestData.Reject(missingId, ErrorCode.Validation);
    }
}
