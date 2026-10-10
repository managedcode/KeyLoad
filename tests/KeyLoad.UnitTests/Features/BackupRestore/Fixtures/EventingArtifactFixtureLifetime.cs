
namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class EventingArtifactFixtureLifetime
{
    private const string RootEvidenceKey = "KeyLoad.EventingArtifact.RetainedRoot";

    internal static async Task RunAsync(EventingArtifactFixture fixture, Func<EventingArtifactFixture, Task> operation)
    {
        var failures = new List<Exception>();
        var fatal = TestDatabaseJoinedLifetime.Observe(fixture.InitializeSource, failures);
        try
        {
            if (failures.Count == 0)
            { fatal ??= await TestDatabaseJoinedLifetime.ObserveAsync(() => operation(fixture), failures); }
        }
        finally
        {
            var targetFatal = TestDatabaseJoinedLifetime.Observe(fixture.JoinTarget, failures);
            fatal ??= targetFatal;
            if (fixture.Source is { } source)
            {
                var sourceFatal = TestDatabaseJoinedLifetime.Observe(source.JoinOwnedResources, failures);
                fatal ??= sourceFatal;
            }
        }
        if (failures.Count == 0)
        { fatal ??= TestDatabaseJoinedLifetime.Observe(() => Directory.Delete(fixture.Root, true), failures); }
        foreach (var failure in failures)
        { failure.Data[RootEvidenceKey] = fixture.Root; }
        TestDatabaseJoinedLifetime.ThrowIfAny(failures, fatal);
    }

}
