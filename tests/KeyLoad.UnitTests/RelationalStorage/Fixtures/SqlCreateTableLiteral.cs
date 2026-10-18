using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Server;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal static class SqlCreateTableLiteral
{
    internal const string Root = "root";
    internal const string IdColumn = "id";
    internal const string Table = "sql-created";
    internal const string Sql = "CREATE TABLE [sql-created] (id NVARCHAR(MAX) NOT NULL PRIMARY KEY, title NVARCHAR(MAX) NOT NULL, amount BIGINT NOT NULL, active BIT NOT NULL)";
    internal const string ChangedSql = "CREATE TABLE [sql-created] (id NVARCHAR(MAX) NOT NULL PRIMARY KEY, title BIT NOT NULL, amount BIGINT NOT NULL, active BIT NOT NULL)";
    internal const string Select = "SELECT TOP (5) title, amount, active FROM [sql-created] ORDER BY id";
    internal const string Row = "{\"id\":\"one\",\"title\":\"native\",\"amount\":7,\"active\":true}";
    internal const string InvalidRow = "{\"id\":\"different\",\"title\":\"native\",\"amount\":7,\"active\":true}";
    internal const string Selected = "{\"title\":\"native\",\"amount\":7,\"active\":true}";
    internal const string One = "one";
    internal const long Revision = 1;
    internal const string AccessPath = "bounded-full-scan";
    internal static ResourceDefinition Definition(PartitionRef partition) => new(Table, ResourceKind.Collection, partition.TransactionDomainId)
    {
        Authority = DocumentAuthority.Document,
        RelationalSchema = new(IdColumn, [new(IdColumn, RelationalColumnType.Text), new("title", RelationalColumnType.Text),
            new("amount", RelationalColumnType.WholeNumber), new("active", RelationalColumnType.Boolean)])
    };
    internal static McpDecodedOperation Compile(PartitionRef partition, Guid id, CancellationToken token)
        => Compile(partition, id, Sql, token);
    internal static McpDecodedOperation Compile(PartitionRef partition, Guid id, string sql, CancellationToken token)
    {
        var limits = UnitExecutionOptions.DatabaseLimits();
        return SqlOperationCompiler.Compile(new(partition, sql, CommandId: id), limits,
            limits.Value.MaxBatchBytes, UnitExecutionOptions.QueryExecution(), token);
    }
    internal static Result<object> Apply(DatabaseEngine database, McpDecodedOperation decoded, DateTimeOffset issued)
        => Apply(database, decoded, issued, Root);
    internal static Result<object> Apply(DatabaseEngine database, McpDecodedOperation decoded, DateTimeOffset issued, string principal)
        => database.Apply(database.CreateCommand(OperationKind.CreateTable, decoded.CommandId, principal, issued, decoded.Payload));
}
