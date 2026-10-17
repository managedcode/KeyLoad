using System.Collections.Immutable;

namespace KeyLoad.Storage;

/// <summary>Owns a bounded ordered page and its logical limit-lookahead result.</summary>
/// <param name="Records">Independent key/value copies in encoded key order.</param>
/// <param name="HasMore">True when another live matching record was examined.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ScanPage)]
public sealed record ScanPage([property: Orleans.Id(0)] ImmutableArray<KeyValueRecord> Records, [property: Orleans.Id(1)] bool HasMore);
