using KeyLoad.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativePreStartDisposeTrial
{
    internal static async Task RunAsync(string[] arguments, NativeLocalhostClusteringOptions localhost,
        RequestCqrsHostedProfile profile, string root, string directory, CancellationToken token)
    {
        var failures = new List<Exception>();
        using var startupObservation = CancellationTokenSource.CreateLinkedTokenSource(token);
        WebApplication? application = null;
        PartitionStorageNativePreStartDisposeOwners? captured = null;
        Task? originalDisposal = null;
        Task? originalStartupWait = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            application = await ServerConfiguration.BuildAsync(arguments, localhostClustering: localhost,
                configureServices: profile.Register, startupCancellation: token).ConfigureAwait(false);
            captured = new(application.Services.GetRequiredService<PartitionHostApplicationOwner>(),
                application.Services.GetRequiredService<OrleansNode>(),
                application.Services.GetRequiredService<ServerConnectionRegistry>());
            await PartitionStorageNativePreStartDisposeAssertions.BeforeAsync(application, captured, root, directory)
                .ConfigureAwait(false);
            originalStartupWait = captured.Native.WaitForNativeStartupAsync(startupObservation.Token);
            await PartitionStorageNativeStartupWaitAssertions.PendingAsync(captured.Native, originalStartupWait, token)
                .ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (application is not null)
        {
            await ServerFailureObserver.ObserveAsync(() =>
            {
                var original = application.DisposeAsync().AsTask();
                originalDisposal = original;
                return original;
            }, failures).ConfigureAwait(false);
        }
        if (originalStartupWait is not null)
        {
            if (originalDisposal is { IsCompletedSuccessfully: true })
            {
                await ServerFailureObserver.ObserveAsync(() => PartitionStorageNativeStartupWaitAssertions.ClosedAsync(
                    originalStartupWait), failures).ConfigureAwait(false);
            }
            else
            {
                await ServerFailureObserver.ObserveAsync(startupObservation.CancelAsync, failures).ConfigureAwait(false);
                await ServerFailureObserver.ObserveAsync(() => originalStartupWait, failures).ConfigureAwait(false);
            }
        }
        if (captured is not null && originalDisposal is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => PartitionStorageNativePreStartDisposeAssertions.AfterAsync(
                originalDisposal, captured, root, directory), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
