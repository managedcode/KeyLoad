namespace KeyLoad.Clustering;

/// <summary>Typed successful internal reply; public JSON is produced only at the Server exit.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(GrainContracts.ValueAlias), global::Orleans.Immutable]
public sealed record GrainValue([property: global::Orleans.Id(GrainContracts.ValueField)] object? /* KLD0041: Keep the current generated reply Value field ID and runtime type header; each closed handler supplies its concrete response once. */ Value);
