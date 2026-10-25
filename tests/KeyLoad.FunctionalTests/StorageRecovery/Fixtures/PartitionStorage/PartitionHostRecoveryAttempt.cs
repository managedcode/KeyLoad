using System.Runtime.ExceptionServices;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class PartitionHostRecoveryAttempt
{
    private const string Unjoined = "An original execution recovery owner is not known released; retain its root.";
    private ExceptionDispatchInfo? originalFailure;
    internal PartitionHostOwnership Ownership { get; } = new();
    internal bool IsUncertain => originalFailure is not null && !Ownership.CanReleaseRoot;
    internal void RecordFailure(Exception failure) => originalFailure = ExceptionDispatchInfo.Capture(failure);

    internal void RequireReleased()
    {
        if (Ownership.CanReleaseRoot)
        {
            return;
        }
        originalFailure?.Throw();
        throw new InvalidOperationException(Unjoined);
    }
}
