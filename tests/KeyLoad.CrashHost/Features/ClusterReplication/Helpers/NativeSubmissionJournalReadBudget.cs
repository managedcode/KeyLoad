using KeyLoad.Replication;
using KeyLoad.Storage;

namespace KeyLoad.CrashHost.Features.ClusterReplication;

internal sealed class NativeSubmissionJournalReadBudget(StorageReadObserver? additionalReadObserver = null)
{
    internal const int MaximumRecordBytes = 8_192;
    private const long NoReadBytes = 0;
    private const long MaximumReadBytes = 32_768;
    private const string Exceeded = "The original submission journal read exceeds its fixed qualification budget.";
    private long examined;

    internal byte[] Required(IKeyValueView view, byte[] key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[]? original = null;
        var found = view.ReadValue(key, bytes => original = bytes.ToArray(), Observe);
        if (!found || original is null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, ReplicaProtocol.CorruptLog); }
        return original;
    }

    private void Observe(long byteCount)
    {
        if (byteCount < NoReadBytes || byteCount > MaximumRecordBytes || byteCount > MaximumReadBytes - examined)
        { throw new InvalidDataException(Exceeded); }
        examined += byteCount;
        additionalReadObserver?.Invoke(byteCount);
    }
}
