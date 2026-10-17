namespace KeyLoad;

/// <summary>Contains an owned projected sample, or an ordinary absent-series result.</summary>
/// <param name="Sample">Latest sample under persisted authorization, or null when absent.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.LatestSampleResult)]
public sealed record LatestSampleResult([property: Orleans.Id(0)] SampleRecord? Sample);
