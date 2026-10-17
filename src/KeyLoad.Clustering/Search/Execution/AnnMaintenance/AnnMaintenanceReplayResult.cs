namespace KeyLoad.Clustering;

internal sealed record AnnMaintenanceReplayResult(ProjectionBatchResult? Checkpoint, CommitProjectionBatchRequest? EmptyIntent);
