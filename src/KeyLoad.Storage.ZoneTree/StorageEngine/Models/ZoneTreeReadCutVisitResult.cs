namespace KeyLoad.Storage.ZoneTree;

internal readonly record struct ZoneTreeReadCutVisitResult(
    int Records,
    long ExaminedBytes,
    long RuntimeAdvances,
    bool HasMore,
    bool StoppedByVisitor);
