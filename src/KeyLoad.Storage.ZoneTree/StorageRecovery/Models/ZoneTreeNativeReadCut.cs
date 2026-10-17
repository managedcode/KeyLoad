namespace KeyLoad.Storage.ZoneTree;

internal sealed record ZoneTreeNativeReadCut(
    int FormatVersion,
    int KeyCodecVersion,
    Guid NodeId,
    Guid Incarnation,
    DurabilityProfile Durability,
    bool DispatchPaused,
    long ReadGeneration,
    long Position);

internal readonly record struct ZoneTreeReadCutVisitResult(
    int Records,
    long ExaminedBytes,
    long NativeAdvances,
    bool HasMore,
    bool StoppedByVisitor);
