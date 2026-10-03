namespace KeyLoad.Orleans;

/// <summary>Typed successful internal reply; public JSON is produced only at the Server exit.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(GrainNativeContracts.ValueAlias)]
public sealed record GrainValue([property: global::Orleans.Id(GrainNativeContracts.ValueField)] object? Value);
