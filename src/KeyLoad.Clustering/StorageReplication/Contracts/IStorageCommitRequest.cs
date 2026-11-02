using ManagedCode.Communication.Commands;

namespace KeyLoad.Clustering;

/// <summary>Ordered command admission and settlement shared by requests with different typed replies.</summary>
internal interface IStorageCommitRequest
{
    Command<DatabaseCommand> Operation { get; }
    bool TrySend();
    bool TryReturn();
    void Complete(StorageCopyOutcome outcome);
    void Release();
}
