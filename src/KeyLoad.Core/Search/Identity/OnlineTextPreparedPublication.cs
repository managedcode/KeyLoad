namespace KeyLoad.Core.Features.Search;

internal sealed record OnlineTextPreparedPublication(
    OnlineTextPublicationPhaseCommand Command,
    NativeTextSeedCapture ValidatedSource,
    CommitProjectionBatchRequest CheckpointIntent,
    DateTimeOffset OriginalExpiry);
