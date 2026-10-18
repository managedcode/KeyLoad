using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryHost.Features.BlobStorage;

internal sealed record BlobCommitCutObservedFault(ErrorCode Code, string Detail, ErrorCode PoisonCode,
    string PoisonDetail, CommitStage Stage, long Position, bool Injected);
