using System.Globalization;
using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateColdProtocol
{
    internal const int Resources = 3;
    internal const string Id = "retained";
    internal const string IdColumn = "id";
    internal const string ReferenceField = "reference";
    internal const string Series = "retained";
    internal const string Edge = "retained-link";
    internal const string Text = "{\"text\":\"needle\"}";
    internal const string Vector = "{\"state\":\"retained\"}";
    internal static readonly DateTimeOffset Start = new(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
    internal static string Resource(string model, int index) => "cold_" + model + "_" + index.ToString(CultureInfo.InvariantCulture);
    internal static string DocumentJson(EntityRef anchor, BlobRef blob)
        => JsonSerializer.Serialize(new
        {
            anchor,
            blob,
            state = "retained"
        }, JsonDefaults.Options);
    internal static string Tags(EntityRef reference)
        => FunctionalGateBoundJson.Serialize(new
        {
            anchor = reference
        });
    internal static EventData Data(EntityRef reference) => new(Id, "retained-link",
        FunctionalGateBoundJson.Serialize(reference));
    internal static Dictionary<string, JsonElement> Parameters(EntityRef reference)
        => new(StringComparer.Ordinal)
        {
            [IdColumn] = JsonSerializer.SerializeToElement(Id, JsonDefaults.Options),
            [ReferenceField] = JsonSerializer.SerializeToElement(JsonSerializer.Serialize(reference, JsonDefaults.Options), JsonDefaults.Options)
        };
    internal static SqlOperationRequest Insert(PartitionRef partition, int index, EntityRef reference)
        => new(partition, "INSERT INTO [" + Resource("table", index)
            + "] (id,[value],[reference]) VALUES (@id,37,@reference)", Parameters(reference), QueryDialectVersion: SqlOperationProtocol.DefaultQueryDialectVersion, CommandId: Guid.NewGuid());
    internal static SqlOperationRequest Select(PartitionRef partition, int index)
        => new(partition, "SELECT TOP (2) [value],[reference] FROM [" + Resource("table", index)
            + "] WHERE id = @id", new(StringComparer.Ordinal)
            {
                [IdColumn] = JsonSerializer.SerializeToElement(Id, JsonDefaults.Options)
            },
            AllowFullScan: true, QueryDialectVersion: 2);
    internal static SampleData[] Samples => [new("sample-a", Start, 2), new("sample-b", Start.AddMinutes(1), 4)];
}
