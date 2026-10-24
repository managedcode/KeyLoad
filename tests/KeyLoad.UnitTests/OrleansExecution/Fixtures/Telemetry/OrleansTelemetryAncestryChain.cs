using System.Diagnostics;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.OrleansExecution;

internal sealed class OrleansTelemetryAncestryChain : IDisposable
{
    private readonly List<Activity> activities = [];

    internal Activity Start(OrleansRuntimeTelemetryFixture telemetry, int localParents)
    {
        var parent = telemetry.StartParentActivity()
            ?? throw new InvalidOperationException(OrleansTelemetryAncestryProtocol.MissingParent);
        activities.Add(parent);
        for (var index = 0; index < localParents; index++)
        {
            var next = new Activity(OrleansTelemetryAncestryProtocol.LocalOperation);
            activities.Add(next);
            next.Start();
        }
        return activities[^1];
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        for (var index = activities.Count - OrleansTelemetryAncestryProtocol.OneLink; index >= 0; index--)
        {
            ServerFailureObserver.Observe(activities[index].Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
