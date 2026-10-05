using System.Collections.Immutable;

namespace KeyLoad.Server;

internal sealed class AdminHttpMetrics(TimeProvider? clock = null)
{
    private readonly Lock gate = new();
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly DateTimeOffset startedAt = (clock ?? TimeProvider.System).GetUtcNow();
    private readonly Guid processInstance = Guid.NewGuid();
    private readonly AdminHttpFailure[] failures = new AdminHttpFailure[AdminDashboardProtocol.RecentFailureLimit];
    private int nextFailure;
    private int retainedFailures;
    private long completed;
    private long failed;
    private double milliseconds;

    internal void Record(TimeSpan elapsed, AdminHttpFailureDetail? failure)
    {
        lock (gate)
        {
            completed++;
            milliseconds += elapsed.TotalMilliseconds;
            if (failure is { } detail)
            { Retain(detail, elapsed); }
        }
    }

    internal AdminHttpSnapshot Snapshot()
    {
        lock (gate)
        { return new(startedAt, processInstance, completed, failed, milliseconds) { RecentFailures = NewestFirst() }; }
    }

    private void Retain(AdminHttpFailureDetail detail, TimeSpan elapsed)
    {
        failed++;
        failures[nextFailure] = new(time.GetUtcNow(), detail.Method, detail.Route, detail.StatusCode, detail.Aborted, elapsed.TotalMilliseconds);
        nextFailure = (nextFailure + 1) % failures.Length;
        retainedFailures = Math.Min(retainedFailures + 1, failures.Length);
    }

    private ImmutableArray<AdminHttpFailure> NewestFirst()
    {
        var builder = ImmutableArray.CreateBuilder<AdminHttpFailure>(retainedFailures);
        for (var age = 1; age <= retainedFailures; age++)
        { builder.Add(failures[(nextFailure - age + failures.Length) % failures.Length]); }
        return builder.MoveToImmutable();
    }
}
