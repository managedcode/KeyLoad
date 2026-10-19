namespace KeyLoad.Storage.ZoneTree;

internal sealed record ZoneTreeReadCut(
    int FormatVersion,
    int KeyCodecVersion,
    Guid NodeId,
    Guid Incarnation,
    DurabilityProfile Durability,
    bool DispatchPaused,
    long ReadGeneration,
    long Position);
