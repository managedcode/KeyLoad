namespace KeyLoad.Server.Features.ClusterRouting;

internal static class OrleansNodeStartupWait
{
    internal static Task WaitAsync(Lock lifecycle, ref Task? startup, CancellationToken cancellationToken)
    {
        Task original;
        lock (lifecycle)
        { original = startup ?? throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable); }
        return original.WaitAsync(cancellationToken);
    }
}
