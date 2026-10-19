namespace KeyLoad;

/// <summary>Selects one maintained execution text generation without granting authority.</summary>
/// <param name="Consumer">Exact persisted projection consumer.</param>
/// <param name="Generation">Required positive projection generation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(TextIndexSelectionProtocol.Alias)]
public sealed record TextIndexSelection(
    [property: Orleans.Id(0)] ProjectionConsumerRef Consumer,
    [property: Orleans.Id(1)] long Generation);
