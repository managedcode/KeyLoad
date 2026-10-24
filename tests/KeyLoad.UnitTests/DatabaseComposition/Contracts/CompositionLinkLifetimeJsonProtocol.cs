using System.Text.Json;

namespace KeyLoad.UnitTests.DatabaseComposition;

internal static class CompositionLinkLifetimeJsonProtocol
{
    internal const string Root = "root";
    internal const string From = "json-life-from";
    internal const string To = "json-life-to";
    internal const string Graph = "json-life-graph";
    internal const string Queue = "json-life-bad";
    internal const string HealthyQueue = "json-life-healthy";
    internal const string First = "first";
    internal const string Second = "second";
    internal const string Message = "input";
    internal const string Prefix = "derived-";
    internal const string Edge = "derived-input";
    internal const string Label = "linked";
    internal const string EmptyJson = "{}";
    internal const string FirstJson = """{"id":"first","name":"first"}""";
    internal const string SecondJson = """{"id":"second","name":"second"}""";
    internal const string InvalidLink = "The queue graph link is invalid.";
    internal const string DuplicateProperty = "Duplicate JSON property names are not allowed.";
    internal const int Duplicate = 5;

    internal static string Payload(EntityRef from, EntityRef to, int? malformed = null)
        => "{\"attributesJson\":\"{}\",\"from\":" + Entity(from, Lifetime(from, malformed))
            + ",\"label\":\"linked\",\"to\":" + Entity(to, Lifetime(to, null)) + "}";

    private static string Lifetime(EntityRef reference, int? malformed)
    {
        var id = JsonSerializer.Serialize(reference.Lifetime!.Incarnation, JsonDefaults.Options);
        return malformed switch
        {
            null => "{\"incarnation\":" + id + "}",
            0 => "null",
            1 => "7",
            2 => "{\"extra\":true,\"incarnation\":" + id + "}",
            3 => "{\"incarnation\":\"" + new string('A', 64) + "\"}",
            4 => "{\"incarnation\":\"" + new string('a', 63) + "\"}",
            Duplicate => "{\"incarnation\":" + id + ",\"incarnation\":" + id + "}",
            _ => throw new ArgumentOutOfRangeException(nameof(malformed))
        };
    }

    private static string Entity(EntityRef reference, string lifetime)
    {
        var partition = reference.Partition;
        var json = JsonSerializer.Serialize(new
        {
            atomicPartitionId = partition.AtomicPartitionId,
            databaseId = partition.DatabaseId,
            partitionKey = partition.PartitionKey,
            tenantId = partition.TenantId,
            transactionDomainId = partition.TransactionDomainId
        }, JsonDefaults.Options);
        return "{\"collection\":" + JsonSerializer.Serialize(reference.Collection, JsonDefaults.Options)
            + ",\"id\":" + JsonSerializer.Serialize(reference.Id, JsonDefaults.Options)
            + ",\"lifetime\":" + lifetime + ",\"partition\":" + json + "}";
    }
}
