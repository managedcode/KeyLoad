using System.Globalization;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlCreateTableRf3Literal
{
    internal const string IdColumn = "id";
    internal const string Table = "sql-created-rf3";
    internal const string ConcurrentTable = "sql-raced-rf3";
    internal const string ConcurrentSql = "CREATE TABLE [sql-raced-rf3] (id NVARCHAR(MAX) NOT NULL PRIMARY KEY, title NVARCHAR(MAX) NOT NULL, amount BIGINT NOT NULL, active BIT NOT NULL)";
    internal const string RepairTable = "sql-repaired-rf3";
    internal const string Sql = "CREATE TABLE [sql-created-rf3] (id NVARCHAR(MAX) NOT NULL PRIMARY KEY, title NVARCHAR(MAX) NOT NULL, amount BIGINT NOT NULL, active BIT NOT NULL)";
    internal const string RepairSql = "CREATE TABLE [sql-repaired-rf3] (id NVARCHAR(MAX) NOT NULL PRIMARY KEY, title NVARCHAR(MAX) NOT NULL, amount BIGINT NOT NULL, active BIT NOT NULL)";
    internal const string CancelTable = "sql-cancelled-rf3";
    internal const string CancelSql = "CREATE TABLE [sql-cancelled-rf3] (id NVARCHAR(MAX) NOT NULL PRIMARY KEY, title NVARCHAR(MAX) NOT NULL, amount BIGINT NOT NULL, active BIT NOT NULL)";
    internal const string ReadSql = "SELECT TOP (5) title, amount, active FROM [sql-created-rf3] ORDER BY id";
    internal const string Row = "{\"id\":\"one\",\"title\":\"native\",\"amount\":7,\"active\":true}";
    internal const string Selected = "{\"title\":\"native\",\"amount\":7,\"active\":true}";
    internal const string InvalidRow = "{\"id\":\"different\",\"title\":\"native\",\"amount\":7,\"active\":true}";
    internal const string One = "one";
    internal const string CommitOperation = "keyload_documents_commit";
    internal const string AmountPath = "/amount";
    internal const string Eight = "8";
    internal const string Nine = "9";
    internal const string EightSelected = "{\"title\":\"native\",\"amount\":8,\"active\":true}";
    internal const string NineSelected = "{\"title\":\"native\",\"amount\":9,\"active\":true}";
    internal const string IdentityPrefix = "SET CONTEXT_INFO 0x";
    internal const string GuidFormat = "N";
    internal const string ValidationState = "22023";
    internal const string ConflictState = "40001";
    internal const string PermissionState = "42501";
    internal const string InvalidIdentity = "SET CONTEXT_INFO 0x00000000000000000000000000000000";
    internal const int Dialect = 2;
    internal const long InitialRevision = 1;
    internal const long AdministratorEpoch = 2;
    internal const string AccessPath = "bounded-full-scan";
    internal const string AlterSql = "ALTER TABLE [sql-created-rf3] ADD extra BIGINT NOT NULL";
    internal const string DropSql = "DROP TABLE [sql-created-rf3]";
    internal static PartitionRef Partition => PostgresRf3State.Partition;
    internal static SqlOperationRequest Request(Guid id, string sql) => new(Partition, sql, CommandId: id);
    internal static ResourceDefinition Definition(string name) => new(name, ResourceKind.Collection, Partition.TransactionDomainId)
    {
        Authority = DocumentAuthority.Document,
        RelationalSchema = new(IdColumn, [new(IdColumn, RelationalColumnType.Text), new("title", RelationalColumnType.Text),
            new("amount", RelationalColumnType.WholeNumber), new("active", RelationalColumnType.Boolean)])
    };
    internal static string Arm(Guid id) => IdentityPrefix + id.ToString(GuidFormat, CultureInfo.InvariantCulture);
}
