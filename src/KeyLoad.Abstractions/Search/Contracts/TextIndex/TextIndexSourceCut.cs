namespace KeyLoad;

/// <summary>Describes the observed canonical source cut; this is not a bearer token.</summary>
/// <param name="NodeId">Actual node-local source owner.</param>
/// <param name="Incarnation">Actual current source incarnation.</param>
/// <param name="Position">Canonical local position.</param>
/// <param name="AppliedPosition">Replicated applied authority at the same cut.</param>
/// <param name="ThroughSequence">Exact canonical source upper.</param>
/// <param name="ReadGeneration">Canonical replacement generation.</param>
/// <param name="SchemaVersion">Current persisted collection schema version.</param>
/// <param name="PolicyEpoch">Current persisted administrator policy epoch.</param>
/// <param name="ResourceSha256">Exact native persisted resource identity digest.</param>
[Orleans.GenerateSerializer, Orleans.Alias(TextIndexMaintenanceAliases.Cut)]
public sealed record TextIndexSourceCut(
    [property: Orleans.Id(0)] Guid NodeId,
    [property: Orleans.Id(1)] Guid Incarnation,
    [property: Orleans.Id(2)] long Position,
    [property: Orleans.Id(3)] long AppliedPosition,
    [property: Orleans.Id(4)] long ThroughSequence,
    [property: Orleans.Id(5)] long ReadGeneration,
    [property: Orleans.Id(6)] long SchemaVersion,
    [property: Orleans.Id(7)] long PolicyEpoch,
    [property: Orleans.Id(8)] string ResourceSha256);
