using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class TestDatabaseDeleteFailureHealthy
{
    internal static async Task RunAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var failures = new List<Exception>();
        TestDatabase? fixture = null;
        var joined = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fixture = new();
            var state = await TestDatabaseConcurrentCloseSeeding.RunAsync(fixture, TestDatabaseConcurrentCloseProtocol.HealthyId);
            joined = true;
            fixture.JoinOwnedResources();
            await TestDatabaseConcurrentCloseReopen.RunAsync(fixture, state);
            fixture.Dispose();
            await Assert.That(Directory.Exists(fixture.Directory)).IsFalse();
            fixture.Dispose();
            fixture.JoinOwnedResources();
        }, failures);
        if (fixture is not null && !joined)
        {
            ServerFailureObserver.Observe(fixture.JoinOwnedResources, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
