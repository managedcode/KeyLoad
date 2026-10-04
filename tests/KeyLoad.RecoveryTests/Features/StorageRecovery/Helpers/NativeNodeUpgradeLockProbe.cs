using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal enum NativeNodeUpgradeLockBoundary
{
    AfterPrepare,
    AfterPriorSourceInspection,
    AfterVerifyPrepared,
    AfterPublish,
    NegativeControl
}

internal static class NativeNodeUpgradeLockProbe
{
    internal const string BoundaryDataKey = "KeyLoad.StorageRecovery.NativeLockProbeBoundary";

    internal static void Verify(string source, NativeNodeUpgradeLockBoundary boundary)
    {
        try
        {
            using var lease = ServerNodeUpgradeLocks.Acquire(source);
        }
        catch (Exception failure) when (failure is IOException or KeyLoadException)
        {
            failure.Data[BoundaryDataKey] = boundary;
            throw;
        }
    }
}
