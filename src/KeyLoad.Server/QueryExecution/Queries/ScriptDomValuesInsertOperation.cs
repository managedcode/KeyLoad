using System.Collections.Immutable;
using System.Text;
using Microsoft.Extensions.Options;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;
namespace KeyLoad.Server;

internal static class ScriptDomValuesInsertOperation
{
    internal static CommandRequest Compile(SqlOperationRequest request, Dom.InsertStatement statement,
        IOptions<DatabaseLimits> limits, int maximumPayloadBytes, int? positionalCount,
        Action checkBudget, CancellationToken token)
    {
        if (request.Partition is null || request.AllowFullScan || request.Cursor is not null)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        if (request.QueryDialectVersion != SqlOperationProtocol.DefaultQueryDialectVersion)
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
        var identity = SqlOriginalMutationIdentity.Required(request.CommandId);
        var (table, columns, values) = ScriptDomValuesInsertShape.Read(statement, checkBudget, token);
        if (values.RowValues.Count > limits.Value.MaxBatchMutations)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RowsExceeded);
        }
        var parameters = new ScriptDomCompositionParameters(request.Parameters, positionalCount, checkBudget, token);
        var mutations = ImmutableArray.CreateBuilder<Mutation>(values.RowValues.Count);
        var remaining = Math.Min(maximumPayloadBytes, limits.Value.MaxBatchBytes);
        foreach (var row in values.RowValues)
        {
            checkBudget();
            token.ThrowIfCancellationRequested();
            var json = ScriptDomInsertRowJson.Read(columns, row, parameters, limits,
                Math.Min(remaining, limits.Value.MaxDocumentBytes), checkBudget, token);
            remaining = checked(remaining - Encoding.UTF8.GetByteCount(json));
            mutations.Add(new InsertTypedRow(table, json));
        }
        parameters.CompleteOptional();
        checkBudget();
        token.ThrowIfCancellationRequested();
        return new(identity, request.Partition, mutations.MoveToImmutable(),
            request.OwnershipEpoch ?? SqlMutationIdentityFields.InitialOwnershipEpoch);
    }
    private const string RowsExceeded = "The SQL INSERT exceeds its existing mutation limit.";
}

