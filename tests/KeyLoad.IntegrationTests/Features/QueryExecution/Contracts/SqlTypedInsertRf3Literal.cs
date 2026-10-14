using System.Text.Json;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlTypedInsertRf3Literal
{
    internal const string Table = "sql-values-rf3";
    internal const string One = "one";
    internal const string Two = "two";
    internal const string Three = "three";
    internal const string Four = "four";
    internal const string Five = "five";
    internal const string Cancelled = "cancelled";
    internal const string FirstCold = "zz-cold-1";
    internal const string SecondCold = "zz-cold-2";
    internal const string IdColumn = "id";
    internal const string TitleColumn = "title";
    internal const string AmountColumn = "amount";
    internal const string ActiveColumn = "active";
    internal const string PriceColumn = "price";
    internal const string TimestampColumn = "at";
    internal const string TitlePath = "/title";
    internal const string ReadGrant = "rows.read";
    internal const string UseGrant = "rows.use";
    internal const string WriteGrant = "rows.write";
    internal const string MissingGrant = "rows.ungranted";
    internal const string Classification = "private";
    internal const string Index = "title-unique";
    internal const string Sql = "INSERT INTO [sql-values-rf3] (id,title,amount,active,price,at) VALUES (N'one',N'alpha',7,@active,12.5,N'2026-10-14T00:00:00Z'),(N'two',N'beta',NULL,NULL,NULL,NULL)";
    internal const string WireSql = "INSERT INTO [sql-values-rf3] (id,title,amount,active,price,at) VALUES (N'one',N'alpha',7,$1,12.5,N'2026-10-14T00:00:00Z'),(N'two',N'beta',NULL,NULL,NULL,NULL)";
    internal const string Duplicate = "INSERT INTO [sql-values-rf3] (id,title,amount) VALUES (N'one',N'changed',8)";
    internal const string Rollback = "INSERT INTO [sql-values-rf3] (id,title,amount) VALUES (N'four',N'delta',4),(N'two',N'changed',5)";
    internal const string UniqueRollback = "INSERT INTO [sql-values-rf3] (id,title,amount) VALUES (N'four',N'delta',4),(N'five',N'alpha',5)";
    internal const string TypeFault = "INSERT INTO [sql-values-rf3] (id,title,amount) VALUES (N'four',N'delta',N'wrong')";
    internal const string MissingFault = "INSERT INTO [sql-values-rf3] (id,amount) VALUES (N'four',4)";
    internal const string Repair = "INSERT INTO [sql-values-rf3] (id,title) VALUES (N'three',N'gamma')";
    internal const string CancelSql = "INSERT INTO [sql-values-rf3] (id,title) VALUES (N'cancelled',N'cancelled')";
    internal const string FirstColdSql = "INSERT INTO [sql-values-rf3] (id,title) VALUES (N'zz-cold-1',N'cold-one')";
    internal const string SecondColdSql = "INSERT INTO [sql-values-rf3] (id,title) VALUES (N'zz-cold-2',N'cold-two')";
    internal const string Read = "SELECT TOP (8) id,title,amount,active,price,at FROM [sql-values-rf3] ORDER BY id";
    internal const string OneJson = "{\"id\":\"one\",\"title\":\"alpha\",\"amount\":7,\"active\":true,\"price\":12.5,\"at\":\"2026-10-14T00:00:00Z\"}";
    internal const string TwoJson = "{\"id\":\"two\",\"title\":\"beta\",\"amount\":null,\"active\":null,\"price\":null,\"at\":null}";
    internal const string ThreeJson = "{\"id\":\"three\",\"title\":\"gamma\",\"amount\":null,\"active\":null,\"price\":null,\"at\":null}";
    internal const string CancelJson = "{\"id\":\"cancelled\",\"title\":\"cancelled\",\"amount\":null,\"active\":null,\"price\":null,\"at\":null}";
    internal const string FirstColdJson = "{\"id\":\"zz-cold-1\",\"title\":\"cold-one\",\"amount\":null,\"active\":null,\"price\":null,\"at\":null}";
    internal const string SecondColdJson = "{\"id\":\"zz-cold-2\",\"title\":\"cold-two\",\"amount\":null,\"active\":null,\"price\":null,\"at\":null}";
    internal const string PutKind = "putDocument";
    internal const int Dialect = 1;
    internal const int MaximumRows = 8;
    internal const long InitialRevision = 1;
    internal static PartitionRef Partition => PostgresRf3State.Partition;
    internal static SqlOperationRequest Request(Guid id, string sql) => new(Partition, sql,
        sql == Sql ? new Dictionary<string, JsonElement>(StringComparer.Ordinal) { [ActiveColumn] = JsonSerializer.SerializeToElement(true) } : null,
        CommandId: id);
    internal static ResourceDefinition Definition => new(Table, ResourceKind.Collection, Partition.TransactionDomainId)
    {
        Authority = DocumentAuthority.Document,
        Indexes = [new(Index, [TitlePath], Unique: true)],
        FieldPolicies = [new(TitlePath, Classification, ReadGrant, UseGrant, WriteGrant)],
        RelationalSchema = new(IdColumn, [new(IdColumn, RelationalColumnType.Text), new(TitleColumn, RelationalColumnType.Text),
            new(AmountColumn, RelationalColumnType.WholeNumber, Nullable: true), new(ActiveColumn, RelationalColumnType.Boolean, Nullable: true),
            new(PriceColumn, RelationalColumnType.FixedPoint, Nullable: true),
            new(TimestampColumn, RelationalColumnType.UtcTimestamp, Nullable: true)])
    };
}
