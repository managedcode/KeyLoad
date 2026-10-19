namespace KeyLoad;

/// <summary>Describes one canonical seed cut; it is not a bearer authorization token.</summary>
/// <param name="NodeId">Exact physical storage owner.</param>
/// <param name="Incarnation">Exact canonical store incarnation.</param>
/// <param name="Position">Observed canonical storage position.</param>
/// <param name="AppliedPosition">Observed applied command authority.</param>
/// <param name="ThroughSequence">Observed canonical outbox upper sequence.</param>
/// <param name="ReadGeneration">Observed execution read generation.</param>
/// <param name="SchemaVersion">Current persisted resource schema version.</param>
/// <param name="PolicyEpoch">Current persisted administrator policy epoch.</param>
/// <param name="CorpusSha256">Execution canonical corpus identity digest.</param>
/// <param name="StoreFormatVersion">Exact canonical storage format at capture.</param>
/// <param name="KeyCodecVersion">Exact canonical key codec at capture.</param>
/// <param name="DependencySha256">Bounded execution resource dependency/policy identity at capture.</param>
/// <param name="OutboxFirstAvailable">Original retained-history lower boundary at this exact cut.</param>
[Orleans.GenerateSerializer, Orleans.Alias(AnnMaintenanceAliases.Cut)]
public sealed record AnnSourceCut(
    [property: Orleans.Id(0)] Guid NodeId,
    [property: Orleans.Id(1)] Guid Incarnation,
    [property: Orleans.Id(2)] long Position,
    [property: Orleans.Id(3)] long AppliedPosition,
    [property: Orleans.Id(4)] long ThroughSequence,
    [property: Orleans.Id(5)] long ReadGeneration,
    [property: Orleans.Id(6)] long SchemaVersion,
    [property: Orleans.Id(7)] long PolicyEpoch,
    [property: Orleans.Id(8)] string CorpusSha256,
    [property: Orleans.Id(9)] int StoreFormatVersion,
    [property: Orleans.Id(10)] int KeyCodecVersion,
    [property: Orleans.Id(11)] string DependencySha256,
    [property: Orleans.Id(12)] long OutboxFirstAvailable);
