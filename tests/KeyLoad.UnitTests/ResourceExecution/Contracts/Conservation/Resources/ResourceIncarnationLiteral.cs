using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.ResourceExecution;

internal static class ResourceIncarnationLiteral
{
    internal const string Table = "identity-records";
    internal const string RowId = "one";
    internal const string IdColumn = "id";
    internal const string RootKey = "ResourceIncarnation.Root";
    internal const string OwnerKey = "ResourceIncarnation.Owner";
    internal const string OwnerLifetimeKey = "ResourceIncarnation.OwnerLifetime";
    internal const string ColdOwnerKey = "ResourceIncarnation.ColdOwner";
    internal const string ColdRootKey = "ResourceIncarnation.ColdRoot";
    internal const string ColdCloseKnownKey = "ResourceIncarnation.CloseKnown";
    internal const string RawCreate = "{\"tenantId\":\"tenant\",\"databaseId\":\"database\",\"definition\":{\"name\":\"identity-records\",\"kind\":\"Collection\",\"transactionDomainId\":\"orders\"}}";
    internal const string Fingerprint = "6582b583af7321db4006d04a19849fc57216bfa68bd44dd6ddb48b2ad4430d50";
    internal const string Incarnation = "100598f0ee06583ef4fe35190f52c113315f2d85c9215e02fcb7e975cb05e437";
    private const string Sql = "CREATE TABLE [identity-records] (id NVARCHAR(MAX) NOT NULL PRIMARY KEY, value BIGINT NOT NULL, secret NVARCHAR(MAX) NOT NULL)";
    internal static readonly Guid OriginalId = new("01234567-89ab-cdef-0123-456789abcdef");

    internal static ResourceDefinition Definition(bool sql) => new(Table, ResourceKind.Collection, "orders")
    {
        RelationalSchema = sql ? new(IdColumn, [new(IdColumn, RelationalColumnType.Text),
            new("value", RelationalColumnType.WholeNumber), new("secret", RelationalColumnType.Text)]) : null
    };

    internal static Command<DatabaseCommand> Creation(TestDatabase fixture, bool sql, CancellationToken token)
    {
        var issued = fixture.Database.EvaluationClock.GetUtcNow();
        if (!sql)
        {
            return fixture.Database.CreateCommand(OperationKind.ConfigureResource, OriginalId, "root", issued, RawCreate);
        }
        var limits = UnitExecutionOptions.DatabaseLimits();
        var decoded = SqlOperationCompiler.Compile(new(fixture.Partition, Sql, CommandId: OriginalId), limits,
            limits.Value.MaxBatchBytes, UnitExecutionOptions.QueryExecution(), token);
        return fixture.Database.CreateCommand(OperationKind.CreateTable, decoded.CommandId, "root", issued, decoded.Payload);
    }

    internal static Command<DatabaseCommand> Configure(DatabaseEngine database, PartitionRef partition, ResourceDefinition definition, long? expected = null)
        => database.CreateCommand(OperationKind.ConfigureResource, Guid.NewGuid(), "root", database.EvaluationClock.GetUtcNow(),
            JsonSerializer.Serialize(new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition)
            {
                ExpectedSchemaVersion = expected
            }, JsonDefaults.Options));

    internal static string CreationJson(Command<DatabaseCommand> original)
        => original.Value!.Kind == OperationKind.ConfigureResource ? RawCreate
            : JsonSerializer.Serialize(OrleansSerialization.Deserialize<CreateTableRequest>(original.Value.Input.Span), JsonDefaults.Options);

    internal static byte[] LifetimeKey(PartitionRef partition, string name = Table)
        => KeyCodec.Encode("resource-lifetime", partition.TenantId, partition.DatabaseId, name);

    internal static string Row(long value) => "{\"id\":\"one\",\"value\":"
        + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"secret\":\"kept\"}";
}
