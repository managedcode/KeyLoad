using Microsoft.Extensions.Options;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;
namespace KeyLoad.Server;

internal static class ScriptDomCreateTableDefinition
{
    internal static CreateTableRequest Read(PartitionRef partition, Dom.CreateTableStatement statement,
        IOptions<DatabaseLimits> configured, Action checkBudget, CancellationToken token)
    {
        var syntax = ScriptDomCreateTableSyntax.Read(statement, configured, checkBudget, token);
        var definition = new ResourceDefinition(syntax.Name, ResourceKind.Collection, partition.TransactionDomainId)
        { Authority = DocumentAuthority.Document, RelationalSchema = syntax.Schema };
        global::KeyLoad.Core.Features.RelationalStorage.RelationalRowValidation.ValidateSchema(definition);
        checkBudget();
        token.ThrowIfCancellationRequested();
        return new(partition.TenantId, partition.DatabaseId, definition);
    }
}
