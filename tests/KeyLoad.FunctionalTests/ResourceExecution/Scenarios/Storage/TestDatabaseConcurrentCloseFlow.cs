using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.ResourceExecution;

internal static class TestDatabaseConcurrentCloseFlow
{
    private const int NoFailures = 0;

    internal static async Task RunAsync(bool joinFirst, CancellationToken token)
    {
        await FixtureAsync(joinFirst, TestDatabaseConcurrentCloseProtocol.OriginalId, token);
        await FixtureAsync(joinFirst: true, TestDatabaseConcurrentCloseProtocol.HealthyId, token);
    }

    private static async Task FixtureAsync(bool joinFirst, Guid commandId, CancellationToken token)
    {
        var failures = new List<Exception>();
        TestDatabase? fixture = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fixture = new();
            var state = await TestDatabaseConcurrentCloseSeeding.RunAsync(fixture, commandId);
            await TestDatabaseConcurrentCloseReopen.HeldAsync(fixture);
            if (joinFirst)
            {
                await TestDatabaseConcurrentCloseCallers.RunAsync(fixture, joinOnly: true, token);
                await TestDatabaseConcurrentCloseReopen.RunAsync(fixture, state);
            }
            await TestDatabaseConcurrentCloseCallers.RunAsync(fixture, joinOnly: false, token);
            await Assert.That(Directory.Exists(fixture.Directory)).IsFalse();
            fixture.Dispose();
            fixture.JoinOwnedResources();
            fixture.Dispose();
            await Assert.That(Directory.Exists(fixture.Directory)).IsFalse();
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
