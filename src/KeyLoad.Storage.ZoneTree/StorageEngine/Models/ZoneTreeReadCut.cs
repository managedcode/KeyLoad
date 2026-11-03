namespace KeyLoad.Storage.ZoneTree;

internal sealed record ZoneTreeReadCut(
    Guid NodeId,
    Guid Incarnation,
    DurabilityProfile Durability,
    bool DispatchPaused,
    long ReadGeneration,
    long Position);
