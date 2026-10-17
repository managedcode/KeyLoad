using KeyLoad.Core;
using KeyLoad.Server;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal static class SqlTypedInsertLiteral
{
    internal const string Root = "root";
    internal const string Writer = "sql-row-writer";
    internal const string Table = "sql-values";
    internal const string One = "one";
    internal const string Two = "two";
    internal const string Three = "three";
    internal const string Four = "four";
    internal const string Five = "five";
    internal const string ReadGrant = "rows.read";
    internal const string UseGrant = "rows.use";
    internal const string WriteGrant = "rows.write";
    internal const string MissingGrant = "rows.ungranted";
    internal const string TitlePath = "/title";
    internal const string IdColumn = "id";
    internal const string TitleColumn = "title";
    internal const string AmountColumn = "amount";
    internal const string ActiveColumn = "active";
    internal const string PriceColumn = "price";
    internal const string TimestampColumn = "at";
    internal const string Classification = "private";
    internal const string UniqueIndex = "title-unique";
    internal const string Sql = "INSERT INTO [sql-values] (id,title,amount,active,price,at) VALUES (N'one',N'alpha',7,@active,12.5,N'2026-10-14T00:00:00Z'),(N'two',N'beta',NULL,NULL,NULL,NULL)";
    internal const string Duplicate = "INSERT INTO [sql-values] (id,title,amount) VALUES (N'one',N'changed',8)";
    internal const string Rollback = "INSERT INTO [sql-values] (id,title,amount) VALUES (N'four',N'delta',4),(N'two',N'changed',5)";
    internal const string UniqueRollback = "INSERT INTO [sql-values] (id,title,amount) VALUES (N'four',N'delta',4),(N'five',N'alpha',5)";
    internal const string Repair = "INSERT INTO [sql-values] (id,title) VALUES (N'three',N'gamma')";
    internal const string Select = "SELECT TOP (8) title FROM [sql-values] ORDER BY id";
    internal const string OneJson = "{\"id\":\"one\",\"title\":\"alpha\",\"amount\":7,\"active\":true,\"price\":12.5,\"at\":\"2026-10-14T00:00:00Z\"}";
    internal const string TwoJson = "{\"id\":\"two\",\"title\":\"beta\",\"amount\":null,\"active\":null,\"price\":null,\"at\":null}";
    internal const string ThreeJson = "{\"id\":\"three\",\"title\":\"gamma\"}";
    internal const string ScopeDenied = "The principal cannot perform this operation in this scope.";
    internal const string EpochChanged = "The principal policy changed since this command was evaluated.";
    internal static ResourceDefinition Definition(PartitionRef partition) => new(Table, ResourceKind.Collection, partition.TransactionDomainId)
    {
        Authority = DocumentAuthority.Document,
        Indexes = [new(UniqueIndex, [TitlePath], Unique: true)],
        FieldPolicies = [new(TitlePath, Classification, ReadGrant, UseGrant, WriteGrant)],
        RelationalSchema = new(IdColumn, [new(IdColumn, RelationalColumnType.Text), new(TitleColumn, RelationalColumnType.Text),
            new(AmountColumn, RelationalColumnType.WholeNumber, Nullable: true), new(ActiveColumn, RelationalColumnType.Boolean, Nullable: true),
            new(PriceColumn, RelationalColumnType.FixedPoint, Nullable: true),
            new(TimestampColumn, RelationalColumnType.UtcTimestamp, Nullable: true)])
    };
    internal static McpDecodedOperation Compile(PartitionRef partition, Guid id, string sql, CancellationToken token)
    {
        var parameters = sql == Sql ? new Dictionary<string, global::System.Text.Json.JsonElement>(StringComparer.Ordinal)
        {
            [ActiveColumn] = global::System.Text.Json.JsonSerializer.SerializeToElement(true)
        } : null;
        var limits = UnitExecutionOptions.DatabaseLimits();
        return SqlOperationCompiler.Compile(new(partition, sql, parameters, CommandId: id), limits,
            limits.Value.MaxBatchBytes, UnitExecutionOptions.QueryExecution(), token);
    }
    internal static OperationResult Apply(DatabaseEngine database, McpDecodedOperation command, DateTimeOffset at)
        => database.Apply(database.CreateNativeOperation(OperationKind.Batch, command.CommandId, Writer, at, command.Payload));
}
