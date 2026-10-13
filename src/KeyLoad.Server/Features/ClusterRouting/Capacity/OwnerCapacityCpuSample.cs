using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class OwnerCapacityCpuSample
{
    private const long MinimumUsageDelta = 0;
    private const decimal MinimumElapsedMicroseconds = 0;
    private const long MicrosecondsPerSecond = 1_000_000;
    private const decimal MaximumIntBasisPoints = int.MaxValue;

    internal static int? Calculate(OwnerCapacityOperatingSystemSample before,
        OwnerCapacityOperatingSystemSample current, TimeProvider clock)
    {
        if (before.CpuQuotaMicroseconds != current.CpuQuotaMicroseconds
            || before.CpuPeriodMicroseconds != current.CpuPeriodMicroseconds
            || current.Timestamp <= before.Timestamp || current.CpuUsageMicroseconds < before.CpuUsageMicroseconds)
        { return null; }
        var elapsed = clock.GetElapsedTime(before.Timestamp, current.Timestamp);
        var microseconds = (decimal)elapsed.Ticks * MicrosecondsPerSecond / TimeSpan.TicksPerSecond;
        if (microseconds <= MinimumElapsedMicroseconds)
        { return null; }
        var delta = checked(current.CpuUsageMicroseconds - before.CpuUsageMicroseconds);
        if (delta < MinimumUsageDelta)
        { return null; }
        var basis = delta * (decimal)current.CpuPeriodMicroseconds * TenantPlacementPolicyProtocol.BasisPoints
            / (microseconds * current.CpuQuotaMicroseconds);
        return basis > MaximumIntBasisPoints ? null : checked((int)decimal.Ceiling(basis));
    }
}
