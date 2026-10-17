namespace KeyLoad.Core.Features.Search;

internal readonly record struct AnnSeedCut(Guid NodeId, Guid Incarnation,
    int StoreFormatVersion, int KeyCodecVersion, long ReadGeneration,
    long Position, long AppliedPosition, long OutboxTail, long OutboxFirstAvailable);
