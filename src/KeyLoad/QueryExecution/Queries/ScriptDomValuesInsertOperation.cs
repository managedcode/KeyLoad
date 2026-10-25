using System.Collections.Immutable;
using System.Text;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;
namespace KeyLoad.Server;

internal static class ScriptDomValuesInsertOperation
{
    internal static CommandRequest Compile(SqlOperationRequest request, Dom.InsertStatement statement, int maximumPayloadBytes, int? positionalCount,
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
        var parameters = new ScriptDomCompositionParameters(request.Parameters, positionalCount, checkBudget, token);
        var mutations = ImmutableArray.CreateBuilder<Mutation>(values.RowValues.Count);
        var remaining = maximumPayloadBytes;
        foreach (var row in values.RowValues)
        {
            checkBudget();
            token.ThrowIfCancellationRequested();
            var json = ScriptDomInsertRowJson.Read(columns, row, parameters,
                remaining, checkBudget, token);
            remaining = checked(remaining - Encoding.UTF8.GetByteCount(json));
            mutations.Add(new InsertTypedRow(table, json));
        }
        parameters.CompleteOptional();
        checkBudget();
        token.ThrowIfCancellationRequested();
        return new(identity, request.Partition, mutations.MoveToImmutable());
    }
}

