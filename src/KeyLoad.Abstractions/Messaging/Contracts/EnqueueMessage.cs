namespace KeyLoad;

/// <summary>Adds a message to a work queue with scheduling and ordering metadata.</summary>
/// <param name="Queue">Identifies the work queue.</param>
/// <param name="MessageId">Identifies the queue message.</param>
/// <param name="PayloadJson">Contains the optional message payload.</param>
/// <param name="HeadersJson">Contains optional JSON headers.</param>
/// <param name="NotBefore">Sets the optional earliest delivery time.</param>
/// <param name="ExpiresAt">Sets the optional expiration time.</param>
/// <param name="OrderingKey">Groups messages that must retain their ordering.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.EnqueueMessage)]
public sealed record EnqueueMessage([property: Orleans.Id(0)] string Queue, [property: Orleans.Id(1)] string MessageId, [property: Orleans.Id(2)] string PayloadJson, [property: Orleans.Id(3)] string HeadersJson = EnqueueMessage.DefaultHeadersJson,
    [property: Orleans.Id(4)] DateTimeOffset? NotBefore = null, [property: Orleans.Id(5)] DateTimeOffset? ExpiresAt = null, [property: Orleans.Id(6)] string? OrderingKey = null) : Mutation(Queue)
{
    private const string DefaultHeadersJson = "{}";
}
