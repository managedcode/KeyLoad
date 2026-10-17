namespace KeyLoad;

/// <summary>Returns message metadata with any requested payload and headers.</summary>
/// <param name="Metadata">Contains the message state metadata.</param>
/// <param name="PayloadJson">Contains the optional message payload.</param>
/// <param name="HeadersJson">Contains optional JSON headers.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.MessageInspection)]
public sealed record MessageInspection([property: Orleans.Id(0)] MessageMetadata Metadata, [property: Orleans.Id(1)] string? PayloadJson, [property: Orleans.Id(2)] string? HeadersJson);
