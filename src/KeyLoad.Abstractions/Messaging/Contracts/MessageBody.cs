namespace KeyLoad;

/// <summary>Stores a queue message payload, headers, ordering key, and fingerprint.</summary>
/// <param name="Id">Identifies the entity, document, key, message, event, or operation.</param>
/// <param name="PayloadJson">Contains the optional message payload.</param>
/// <param name="HeadersJson">Contains optional JSON headers.</param>
/// <param name="OrderingKey">Groups messages that must retain their ordering.</param>
/// <param name="Fingerprint">Identifies the canonical command fingerprint.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.MessageBody)]
public sealed record MessageBody([property: Orleans.Id(0)] string Id, [property: Orleans.Id(1)] string PayloadJson, [property: Orleans.Id(2)] string HeadersJson, [property: Orleans.Id(3)] string? OrderingKey, [property: Orleans.Id(4)] string Fingerprint);
