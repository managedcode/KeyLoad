namespace KeyLoad.Core.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(Bm25CorpusProtocol.PhaseAlias)]
internal sealed record Bm25CorpusPublicationPhaseCommand(
    [property: global::Orleans.Id(0)] Guid CommandId,
    [property: global::Orleans.Id(1)] Bm25CorpusCurrent Current,
    [property: global::Orleans.Id(2)] Guid? ExpectedGeneration,
    [property: global::Orleans.Id(3)] DateTimeOffset OriginalExpiry,
    [property: global::Orleans.Id(4)] AtomicPartitionPlacementResolution SourcePlacement);
