using KeyLoad.FunctionalTests.ResourceExecution;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.TestInfrastructure;

internal static class TestDatabaseConstructorRollbackHealthy
{
    private const int NoFailures = 0;

    internal static async Task RunAsync(string directory, CancellationToken token)
    {
        var failures = new List<Exception>();
        TestDatabase? fixture = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fixture = new(directory: directory);
            var state = await TestDatabaseConcurrentCloseSeeding.RunAsync(fixture,
                TestDatabaseConcurrentCloseProtocol.HealthyId);
            await TestDatabaseConcurrentCloseReopen.HeldAsync(fixture);
            await TestDatabaseConcurrentCloseCallers.RunAsync(fixture, joinOnly: true, token);
            await TestDatabaseConcurrentCloseReopen.RunAsync(fixture, state);
            await TestDatabaseConcurrentCloseCallers.RunAsync(fixture, joinOnly: false, token);
            await Assert.That(Directory.Exists(directory)).IsFalse();
            fixture.Dispose();
            fixture.JoinOwnedResources();
            fixture.Dispose();
            await Assert.That(Directory.Exists(directory)).IsFalse();
        }, failures);
        if (fixture is not null)
        {
            ServerFailureObserver.Observe(fixture.JoinOwnedResources, failures);
            if (failures.Count == NoFailures)
            {
                ServerFailureObserver.Observe(fixture.Dispose, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
