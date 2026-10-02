using System.Collections.Immutable;
using System.Globalization;

namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal static class RelationalTestData
{
    internal const string Root = "root";
    internal const string Table = "typed-rows";
    internal const string Key = "key";
    internal const string Name = "name";
    internal const string Count = "count";
    internal const string Amount = "amount";
    internal const string Active = "active";
    internal const string Timestamp = "at";
    internal const string Note = "note";
    internal const string First = "first";
    internal const string Second = "second";
    internal const string Domain = "orders";
    internal const string NamePath = "/name";
    internal const string CountPath = "/count";
    internal const string AmountPath = "/amount";
    internal const string KeyPath = "/key";
    internal const string IndexName = "by-name";
    internal const string RowTemplate = "{{\"key\":\"{0}\",\"name\":\"alpha\",\"count\":1,\"amount\":1.25,\"active\":true,\"at\":\"2026-10-02T12:00:00Z\"}}";
    internal const string EmptyJson = "{}";
    internal static ImmutableArray<RelationalColumn> Columns =>
    [
        new(Key, RelationalColumnType.Text), new(Name, RelationalColumnType.Text),
        new(Count, RelationalColumnType.Int64), new(Amount, RelationalColumnType.Decimal),
        new(Active, RelationalColumnType.Boolean), new(Timestamp, RelationalColumnType.UtcTimestamp),
        new(Note, RelationalColumnType.Text, Nullable: true)
    ];

    internal static ResourceDefinition Definition(RelationalSchema? schema = null) => new(Table, ResourceKind.Collection, Domain)
    {
        RelationalSchema = schema ?? new(Key, Columns),
        Indexes = [new(IndexName, [NamePath], Unique: true)]
    };

    internal static ResourceDefinition Configure(TestDatabase database, ResourceDefinition? definition = null)
        => ConfigureResult(database, definition ?? Definition()).Get<ResourceDefinition>();

    internal static OperationResult ConfigureResult(TestDatabase database, ResourceDefinition definition)
        => database.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId, definition));

    internal static string Row(string id = First) => string.Format(CultureInfo.InvariantCulture, RowTemplate, id);

    internal static OperationResult Submit(TestDatabase database, params Mutation[] mutations)
    {
        var id = Guid.NewGuid();
        return database.Submit(OperationKind.Batch, new CommandRequest(id, database.Partition, [.. mutations]), id: id);
    }
}
