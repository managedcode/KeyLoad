using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal sealed record BlobCommitCutObservedFault(ErrorCode Code, string Detail, ErrorCode PoisonCode,
    string PoisonDetail, CommitStage Stage, long Position, bool Injected);
