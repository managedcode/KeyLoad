using System.Text.Json;
using KeyLoad.Query;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Compiles one admitted SQL envelope to the sole canonical operation decoder, without execution.</summary>
internal static class SqlOperationCompiler
{
    internal static McpDecodedOperation Compile(SqlOperationRequest request, IOptions<DatabaseLimits> limitsOptions,
        int maximumPayloadBytes, IOptions<QueryExecutionOptions> queryOptions, CancellationToken cancellationToken = default)
        => RequireOperation(CompileCore(request, limitsOptions, maximumPayloadBytes, queryOptions, null, false, cancellationToken));
    internal static McpDecodedOperation CompileNativeSession(SqlOperationRequest request, IOptions<DatabaseLimits> limitsOptions,
        int maximumPayloadBytes, IOptions<QueryExecutionOptions> queryOptions, int positionalParameterCount,
        CancellationToken cancellationToken = default)
        => RequireOperation(CompileCore(request, limitsOptions, maximumPayloadBytes, queryOptions, positionalParameterCount, false, cancellationToken));
    internal static SqlNativeSessionCompilation CompileNativeSessionPlan(SqlOperationRequest request,
        IOptions<DatabaseLimits> limitsOptions, int maximumPayloadBytes, IOptions<QueryExecutionOptions> queryOptions,
        int positionalParameterCount, CancellationToken cancellationToken)
        => CompileCore(request, limitsOptions, maximumPayloadBytes, queryOptions, positionalParameterCount, true, cancellationToken);
    private static McpDecodedOperation RequireOperation(SqlNativeSessionCompilation compilation)
        => compilation.Operation ?? throw ScriptDomOperationErrors.Unsupported();
    private static SqlNativeSessionCompilation CompileCore(SqlOperationRequest request, IOptions<DatabaseLimits> limitsOptions,
        int maximumPayloadBytes, IOptions<QueryExecutionOptions> queryOptions, int? positionalParameterCount, bool nativeIdentityControl, CancellationToken token)
    {
        SqlOperationBounds.Validate(request, limitsOptions, maximumPayloadBytes, queryOptions, token);
        SqlOriginalMutationIdentity.ValidateDeclared(request.CommandId);
        if (request.OwnershipEpoch < SqlMutationIdentityFields.InitialOwnershipEpoch)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        if (positionalParameterCount is < MinimumNativeParameterCount || positionalParameterCount > queryOptions.Value.MaximumParameters)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        var budget = new global::KeyLoad.Core.ReadExecutionBudget(limitsOptions, cancellationToken: token);
        var statement = global::KeyLoad.Features.QueryExecution.ScriptDomSyntax.Parse(request.Sql, limitsOptions, budget.Check, token);
        if (positionalParameterCount is { } count)
        {
            global::KeyLoad.Features.QueryExecution.ScriptDomSyntax.ValidateNativeSessionSlots(statement, count, budget.Check, token);
        }
        if (nativeIdentityControl && statement is global::Microsoft.SqlServer.TransactSql.ScriptDom.SetCommandStatement control)
        {
            if (request.CommandId.HasValue || request.OwnershipEpoch.HasValue || request.AllowFullScan || request.Cursor is not null
                || request.Parameters is { Count: > MinimumNativeParameterCount }
                || positionalParameterCount != MinimumNativeParameterCount)
            {
                throw ScriptDomOperationErrors.Invalid();
            }
            var controlIdentity = ScriptDomContextIdentity.ReadControl(control, budget.Check, token);
            return new(null, controlIdentity.Identity, controlIdentity.OwnershipEpoch);
        }
        if (request.OwnershipEpoch.HasValue && statement is not global::Microsoft.SqlServer.TransactSql.ScriptDom.InsertStatement
            { InsertSpecification.InsertSource: global::Microsoft.SqlServer.TransactSql.ScriptDom.ValuesInsertSource })
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        var operation = statement switch
        {
            global::Microsoft.SqlServer.TransactSql.ScriptDom.SelectStatement { Into: null } => Select(request, statement,
                maximumPayloadBytes, limitsOptions, queryOptions, budget, positionalParameterCount, token),
            global::Microsoft.SqlServer.TransactSql.ScriptDom.ExecuteStatement execute => ScriptDomExecOperation.Compile(request,
                execute, maximumPayloadBytes, positionalParameterCount, budget.Check, token),
            global::Microsoft.SqlServer.TransactSql.ScriptDom.InsertStatement insert => Composition(request, insert,
                maximumPayloadBytes, limitsOptions, positionalParameterCount, budget.Check, token),
            global::Microsoft.SqlServer.TransactSql.ScriptDom.CreateTableStatement table => ScriptDomCreateTableOperation.Compile(
                request, table, limitsOptions, maximumPayloadBytes, budget.Check, token),
            _ => throw ScriptDomOperationErrors.Unsupported()
        };
        return new(SqlOriginalMutationIdentity.Require(request.CommandId, operation), null);
    }
    private static McpDecodedOperation Select(SqlOperationRequest request,
        global::Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement statement, int maximumPayloadBytes,
        IOptions<DatabaseLimits> limitsOptions, IOptions<QueryExecutionOptions> queryOptions,
        global::KeyLoad.Core.ReadExecutionBudget budget, int? positionalParameterCount, CancellationToken token)
    {
        if (request.Partition is null)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        var query = new QueryRequest(request.Partition, request.Sql, request.Parameters,
            request.AllowFullScan, request.Cursor, request.QueryDialectVersion);
        if (positionalParameterCount is null)
        {
            return Decode(McpToolNames.QueryExecute, query, maximumPayloadBytes, budget.Check, token);
        }
        var ast = global::KeyLoad.Query.Features.QueryExecution.ScriptDomQueryAdapter.Lower(statement, query,
            limitsOptions, queryOptions, budget, positionalParameterCount, token);
        return Decode(McpToolNames.QueryAst, ast, maximumPayloadBytes, budget.Check, token);
    }
    private static McpDecodedOperation Composition(SqlOperationRequest request,
        global::Microsoft.SqlServer.TransactSql.ScriptDom.InsertStatement statement, int maximumPayloadBytes,
        IOptions<DatabaseLimits> limitsOptions, int? positionalParameterCount, Action checkBudget, CancellationToken token)
    {
        var command = statement.InsertSpecification.InsertSource is global::Microsoft.SqlServer.TransactSql.ScriptDom.ValuesInsertSource
            ? ScriptDomValuesInsertOperation.Compile(request, statement, limitsOptions, maximumPayloadBytes, positionalParameterCount, checkBudget, token)
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
    private const int MinimumNativeParameterCount = 0;
}

