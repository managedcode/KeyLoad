namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal sealed record ClusterRestoreRf3OperatorNode(Guid SourceOwnerId, Guid TargetOwnerId, string VoterId,
    string RelativeDataDirectory, Guid NodeId, Guid Incarnation, bool DispatchPaused);
