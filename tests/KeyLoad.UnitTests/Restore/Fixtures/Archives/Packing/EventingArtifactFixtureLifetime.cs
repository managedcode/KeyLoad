using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class EventingArtifactFixtureLifetime
{
    private const string RootEvidenceKey = "KeyLoad.EventingArtifact.RetainedRoot";

    internal static async Task RunAsync(EventingArtifactFixture fixture, Func<EventingArtifactFixture, Task> operation)
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(fixture.InitializeSource, failures);
        try
        {
            if (failures.Count == 0)
            {
                await ServerFailureObserver.ObserveAsync(() => operation(fixture), failures);
            }
        }
        finally
        {
            ServerFailureObserver.Observe(fixture.JoinTarget, failures);
            if (fixture.Source is { } source)
            {
                ServerFailureObserver.Observe(source.JoinOwnedResources, failures);
            }
        }
        if (failures.Count == 0)
        {
            ServerFailureObserver.Observe(() => Directory.Delete(fixture.Root, true), failures);
        }
        foreach (var failure in failures)
        {
            failure.Data[RootEvidenceKey] = fixture.Root;
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

}
