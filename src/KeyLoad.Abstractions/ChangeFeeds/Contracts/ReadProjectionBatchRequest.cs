namespace KeyLoad;

/// <summary>Requests a bounded projection batch.</summary>
/// <param name="Consumer">The projection consumer to read for.</param>
/// <param name="Limit">The maximum number of outbox entries to return.</param>
/// <param name="MaxBytes">The maximum batch size in bytes.</param>
/// <param name="ThroughSequence">Optional inclusive retained upper sequence; null reads through the current head.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReadProjectionBatchRequest)]
public sealed record ReadProjectionBatchRequest([property: Orleans.Id(0)] ProjectionConsumerRef Consumer, [property: Orleans.Id(1)] int Limit = ReadProjectionBatchRequest.DefaultLimit, [property: Orleans.Id(2)] int MaxBytes = ReadProjectionBatchRequest.DefaultMaxBytes,
    [property: Orleans.Id(3), System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] long? ThroughSequence = null)
{
    private const int DefaultLimit = 100;
    private const int DefaultMaxBytes = 4_194_304;
}
