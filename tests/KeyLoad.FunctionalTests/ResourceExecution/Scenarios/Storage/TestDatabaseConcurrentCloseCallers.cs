using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.ResourceExecution;

internal static class TestDatabaseConcurrentCloseCallers
{
    internal static async Task RunAsync(TestDatabase fixture, bool joinOnly, CancellationToken token)
    {
        using var ready = new CountdownEvent(TestDatabaseConcurrentCloseProtocol.Callers);
        using var release = new ManualResetEventSlim(false);
        var workers = new List<Task>(TestDatabaseConcurrentCloseProtocol.Callers);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            for (var index = TestDatabaseConcurrentCloseProtocol.FirstCaller;
                index < TestDatabaseConcurrentCloseProtocol.Callers; index++)
            {
                var ordinal = index;
                workers.Add(Task.Factory.StartNew(() =>
                {
                    ready.Signal();
                    release.Wait(token);
                    if (joinOnly || ordinal % TestDatabaseConcurrentCloseProtocol.CallerParity == TestDatabaseConcurrentCloseProtocol.FirstCaller)
                    {
                        fixture.JoinOwnedResources();
                    }
                    else
                    {
                        fixture.Dispose();
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));
            }
            await Task.Run(() => ready.Wait(token), CancellationToken.None);
        }, failures);
        ServerFailureObserver.Observe(release.Set, failures);
        foreach (var original in workers)
        {
            await ServerFailureObserver.ObserveAsync(() => original, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(workers.Count).IsEqualTo(TestDatabaseConcurrentCloseProtocol.Callers);
        foreach (var original in workers)
        {
            await Assert.That(original.IsCompletedSuccessfully).IsTrue();
        }
    }
}
