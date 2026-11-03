namespace KeyLoad.Core.Search;

internal readonly record struct AnnSeedCut(Guid NodeId, Guid Incarnation,
    long ReadGeneration,
    long Position, long AppliedPosition, long OutboxTail, long OutboxFirstAvailable);
