using KeyLoad.Server;
using Microsoft.AspNetCore.Builder;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class RuntimeConstructorCycleFlow
{
    private const string RootPrefix = "keyload-execution-constructor-cycle-";
    private const string GuidFormat = "N";

    internal static async Task RunAsync(CancellationToken token)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, RootPrefix + Guid.NewGuid().ToString(GuidFormat));
        var failures = new List<Exception>();
        WebApplication? application = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            application = await ServerConfiguration.BuildAsync(RuntimeConstructorCycleArguments.Create(directory),
                startupCancellation: token);
            await RuntimeConstructorCycleBorrowers.RunAsync(application.Services, directory, token);
        }, failures);
        if (application is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => application.DisposeAsync().AsTask(), failures);
        }
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(Directory.Exists(directory)).IsFalse(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
