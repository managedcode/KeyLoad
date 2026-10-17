namespace KeyLoad.Storage.ZoneTree;

internal readonly record struct ZoneTreeReadCutVisitResult(
    int Records,
    long ExaminedBytes,
    long NativeAdvances,
    bool HasMore,
    bool StoppedByVisitor);
