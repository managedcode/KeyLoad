using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;
namespace KeyLoad.Server;

internal static class ScriptDomCreateTableDefinition
{
    internal static CreateTableRequest Read(PartitionRef partition, Dom.CreateTableStatement statement, Action checkBudget, CancellationToken token)
    {
        var syntax = ScriptDomCreateTableSyntax.Read(statement, checkBudget, token);
        var definition = new ResourceDefinition(syntax.Name, ResourceKind.Collection, partition.TransactionDomainId)
        {
            Authority = DocumentAuthority.Document,
            RelationalSchema = syntax.Schema
        };
        global::KeyLoad.Core.RelationalStorage.RelationalRowValidation.ValidateSchema(definition);
        checkBudget();
        token.ThrowIfCancellationRequested();
        return new(partition.TenantId, partition.DatabaseId, definition);
    }
}
