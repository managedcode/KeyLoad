using System.Collections.Immutable;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class AdminHttpMetrics
{
    private readonly Lock gate = new();
    private readonly TimeProvider time;
    private readonly DateTimeOffset startedAt;
    private readonly Guid processInstance = Guid.NewGuid();
    private readonly AdminHttpFailure[] failures;
    private int nextFailure;
    private int retainedFailures;
    private long completed;
    private long failed;
    private double milliseconds;

    public AdminHttpMetrics(IOptions<AdminObservationOptions> observationOptions, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(observationOptions);
        var observation = observationOptions.Value;
        observation.Validate();
        time = clock ?? TimeProvider.System;
        startedAt = time.GetUtcNow();
        failures = new AdminHttpFailure[observation.MaximumRecentFailures];
    }

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
        const int NextFailureStep = 1;
        const int RetainedFailuresStep = 1;

        failed++;
        failures[nextFailure] = new(time.GetUtcNow(), detail.Method, detail.Route, detail.StatusCode, detail.Aborted, elapsed.TotalMilliseconds);
        nextFailure = (nextFailure + NextFailureStep) % failures.Length;
        retainedFailures = Math.Min(retainedFailures + RetainedFailuresStep, failures.Length);
    }

    private ImmutableArray<AdminHttpFailure> NewestFirst()
    {
        const int AgeInitialValue = 1;

        var builder = ImmutableArray.CreateBuilder<AdminHttpFailure>(retainedFailures);
        for (var age = AgeInitialValue; age <= retainedFailures; age++)
        { builder.Add(failures[(nextFailure - age + failures.Length) % failures.Length]); }
        return builder.MoveToImmutable();
    }
}
