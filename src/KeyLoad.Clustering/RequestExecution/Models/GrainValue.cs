namespace KeyLoad.Clustering;

/// <summary>Typed successful internal reply; public JSON is produced only at the Server exit.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(GrainContracts.ValueAlias)]
public sealed record GrainValue([property: global::Orleans.Id(GrainContracts.ValueField)] object? Value);
