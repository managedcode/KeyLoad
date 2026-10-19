namespace KeyLoad.Core.Features.Search;

internal sealed record OnlineTextPreparedPublication(
    OnlineTextPublicationPhaseCommand Command,
    TextSeedCapture ValidatedSource,
    CommitProjectionBatchRequest CheckpointIntent,
    DateTimeOffset OriginalExpiry);
