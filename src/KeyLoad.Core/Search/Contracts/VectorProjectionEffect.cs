namespace KeyLoad.Core.Features.Search;

/// <summary>Retains the exact effect fingerprint and stable mutation receipt for an active projection.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(VectorProjectionNativeAliases.Effect)]
internal sealed record VectorProjectionEffect(
    [property: global::Orleans.Id(VectorProjectionEffectFields.Fingerprint)] string Fingerprint,
    [property: global::Orleans.Id(VectorProjectionEffectFields.Receipt)] MutationReceipt Receipt);
