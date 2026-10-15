using KeyLoad.Server;
using Microsoft.AspNetCore.Builder;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeRuntimeConstructorCycleFlow
{
    private const string RootPrefix = "keyload-native-constructor-cycle-";
    private const string GuidFormat = "N";

    internal static async Task RunAsync(CancellationToken token)
    {
        var directory = Path.Combine(Path.GetTempPath(), RootPrefix + Guid.NewGuid().ToString(GuidFormat));
        var failures = new List<Exception>();
        WebApplication? application = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            application = await ServerConfiguration.BuildAsync(NativeRuntimeConstructorCycleArguments.Create(directory),
                startupCancellation: token);
            await NativeRuntimeConstructorCycleBorrowers.RunAsync(application.Services, directory, token);
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
