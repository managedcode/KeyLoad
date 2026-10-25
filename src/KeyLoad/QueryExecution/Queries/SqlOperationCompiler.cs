using System.Text.Json;
using KeyLoad.Query;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Compiles one admitted SQL envelope to the sole canonical operation decoder, without execution.</summary>
internal static class SqlOperationCompiler
{
    internal static McpDecodedOperation Compile(SqlOperationRequest request,
        int maximumPayloadBytes, IOptions<QueryExecutionOptions> queryOptions, CancellationToken cancellationToken = default)
        => RequireOperation(CompileCore(request, maximumPayloadBytes, queryOptions, null, false, cancellationToken));
    internal static McpDecodedOperation CompileSession(SqlOperationRequest request,
        int maximumPayloadBytes, IOptions<QueryExecutionOptions> queryOptions, int positionalParameterCount,
        CancellationToken cancellationToken = default)
        => RequireOperation(CompileCore(request, maximumPayloadBytes, queryOptions, positionalParameterCount, false, cancellationToken));
    internal static SqlSessionCompilation CompileSessionPlan(SqlOperationRequest request, int maximumPayloadBytes, IOptions<QueryExecutionOptions> queryOptions,
        int positionalParameterCount, CancellationToken cancellationToken)
        => CompileCore(request, maximumPayloadBytes, queryOptions, positionalParameterCount, true, cancellationToken);
    private static McpDecodedOperation RequireOperation(SqlSessionCompilation compilation)
        => compilation.Operation ?? throw ScriptDomOperationErrors.Unsupported();
    private static SqlSessionCompilation CompileCore(SqlOperationRequest request,
        int maximumPayloadBytes, IOptions<QueryExecutionOptions> queryOptions, int? positionalParameterCount, bool identityControl, CancellationToken token)
    {
        SqlOperationBounds.Validate(request, maximumPayloadBytes, queryOptions, token);
        SqlOriginalMutationIdentity.ValidateDeclared(request.CommandId);
        if (positionalParameterCount is < 0 || positionalParameterCount > queryOptions.Value.MaximumParameters)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        var budget = new global::KeyLoad.Core.ReadExecutionBudget(cancellationToken: token);
        var statement = global::KeyLoad.QueryExecution.ScriptDomSyntax.Parse(request.Sql, budget.Check, token);
        if (positionalParameterCount is { } count)
        {
            global::KeyLoad.QueryExecution.ScriptDomSyntax.ValidateSessionSlots(statement, count, budget.Check, token);
        }
        if (identityControl && statement is global::Microsoft.SqlServer.TransactSql.ScriptDom.SetCommandStatement control)
        {
            if (request.CommandId.HasValue || request.AllowFullScan || request.Cursor is not null
                || request.Parameters is { Count: > 0 }
                || positionalParameterCount != 0)
            {
                throw ScriptDomOperationErrors.Invalid();
            }
            return new(null, ScriptDomContextIdentity.Read(control, budget.Check, token));
        }
        var operation = statement switch
        {
            global::Microsoft.SqlServer.TransactSql.ScriptDom.SelectStatement { Into: null } => Select(request, statement,
                maximumPayloadBytes, queryOptions, budget, positionalParameterCount, token),
            global::Microsoft.SqlServer.TransactSql.ScriptDom.ExecuteStatement execute => ScriptDomExecOperation.Compile(request,
                execute, maximumPayloadBytes, positionalParameterCount, budget.Check, token),
            global::Microsoft.SqlServer.TransactSql.ScriptDom.InsertStatement insert => Composition(request, insert,
                maximumPayloadBytes, positionalParameterCount, budget.Check, token),
            global::Microsoft.SqlServer.TransactSql.ScriptDom.DropTableStatement drop => ScriptDomDropTableOperation.Compile(
                request, drop, maximumPayloadBytes, budget.Check, token),
            global::Microsoft.SqlServer.TransactSql.ScriptDom.CreateTableStatement table => ScriptDomCreateTableOperation.Compile(
                request, table, maximumPayloadBytes, budget.Check, token),
            _ => throw ScriptDomOperationErrors.Unsupported()
        };
        return new(SqlOriginalMutationIdentity.Require(request.CommandId, operation), null);
    }
    private static McpDecodedOperation Select(SqlOperationRequest request,
        global::Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement statement, int maximumPayloadBytes, IOptions<QueryExecutionOptions> queryOptions,
        global::KeyLoad.Core.ReadExecutionBudget budget, int? positionalParameterCount, CancellationToken token)
    {
        if (request.Partition is null)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        var query = new QueryRequest(request.Partition, request.Sql, request.Parameters,
            request.AllowFullScan, request.Cursor, request.QueryDialectVersion);
        var ast = global::KeyLoad.Query.QueryExecution.ScriptDomQueryAdapter.Lower(statement, query, queryOptions, budget, positionalParameterCount, token);
        return Decode(McpToolNames.QueryAst, ast, maximumPayloadBytes, budget.Check, token);
    }
    private static McpDecodedOperation Composition(SqlOperationRequest request,
        global::Microsoft.SqlServer.TransactSql.ScriptDom.InsertStatement statement, int maximumPayloadBytes, int? positionalParameterCount, Action checkBudget, CancellationToken token)
    {
        var command = statement.InsertSpecification.InsertSource is global::Microsoft.SqlServer.TransactSql.ScriptDom.ValuesInsertSource
            ? ScriptDomValuesInsertOperation.Compile(request, statement, maximumPayloadBytes, positionalParameterCount, checkBudget, token)
            : SqlCompositionInsert.Compile(request, statement, positionalParameterCount, checkBudget, token);
        return Decode(McpToolNames.DocumentsCommit, command, maximumPayloadBytes, checkBudget, token);
    }
    private static McpDecodedOperation Decode<T>(string name, T request, int maximumPayloadBytes,
        Action checkBudget, CancellationToken token)
    {
        checkBudget();
        token.ThrowIfCancellationRequested();
        if (!McpOperationCatalog.TryGet(name, out var descriptor)
            || descriptor.ReadKind.HasValue == descriptor.CommandKind.HasValue)
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [McpCatalogProtocol.Request] = JsonSerializer.SerializeToElement(request, JsonDefaults.Options)
        };
        var operation = descriptor.Decode(arguments, maximumPayloadBytes);
        checkBudget();
        token.ThrowIfCancellationRequested();
        return operation;
    }
}

