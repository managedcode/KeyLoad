using System.Globalization;
namespace KeyLoad.IntegrationTests.Features.RelationalStorage;
internal static class SqlTypedInsertMovementProtocol
{
    internal const string Table = "moved-sql-rows";
    internal const string One = "source-row";
    internal const string Two = "target-row";
    internal const string OneJson = "{\"id\":\"source-row\",\"title\":\"before-move\"}";
    internal const string TwoJson = "{\"id\":\"target-row\",\"title\":\"after-repair\"}";
    internal const string BeforeSql = "INSERT INTO [moved-sql-rows] (id,title) VALUES (N'source-row',N'before-move')";
    internal const string AfterSql = "INSERT INTO [moved-sql-rows] (id,title) VALUES (N'target-row',N'after-repair')";
    internal const string ReadSql = "SELECT TOP(2) id,title FROM [moved-sql-rows] ORDER BY id";
    internal const string Id = "id";
    internal const string Title = "title";
    internal const string Put = "putDocument";
    internal const long Revision = 1;
    internal const long InitialEpoch = 1;
    internal const long PublishedEpoch = 2;
    internal const long FutureEpoch = 3;
    internal const int Version = 1;
    internal const int SecretBytes = 32;
    internal const string OwnershipState = "08006";
    internal const string ValidationState = "22023";
    internal const string PermissionState = "42501";
    internal const string ConflictState = "40001";
    internal const string FullScan = "bounded-full-scan";
    internal const string Missing = "The original moved SQL state is absent.";
    internal static readonly Guid OriginalId = new("503b4639-a2a8-454b-929c-2168dfe09d8d");
    internal static readonly Guid FailedId = new("db222a8a-cbf6-41c1-9d9b-f82949102543");
    internal static readonly Guid HealthyId = new("db01af02-9d7f-4395-a4f5-c3e949f08adf");
    internal static readonly Guid StaleId = new("74f5b6e8-3052-48e6-99e2-42ec3402429d");
    internal static readonly Guid FutureId = new("e775d954-95f9-4ee5-96b3-289460a7f2a8");
    internal static string Arm(Guid id,long epoch) => "SET CONTEXT_INFO 0x" + id.ToString("N")
        + epoch.ToString("X16",CultureInfo.InvariantCulture);
    internal static ResourceDefinition Definition(PartitionRef partition) => new(Table,ResourceKind.Collection,
        partition.TransactionDomainId) { RelationalSchema = new(Id,[new(Id,RelationalColumnType.Text),new(Title,RelationalColumnType.Text)]) };
    internal static SqlOperationRequest Request(PartitionRef partition,Guid id,string sql,long epoch)
        => new(partition,sql,CommandId:id,OwnershipEpoch:epoch);
}

