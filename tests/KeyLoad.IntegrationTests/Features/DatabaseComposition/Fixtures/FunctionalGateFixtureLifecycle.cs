using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed partial class FunctionalGateFixture
{
    public ValueTask DisposeAsync()
    {
        lock (disposalGate)
        {
            return new(disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        // Publish the one original cleanup task before a native cancellation callback can reenter.
        await Task.Yield();
        var failures = new List<Exception>();
        var stopped = App is null;
        using var cleanup = imageSession?.CreateApplicationCleanupCancellation()
            ?? new CancellationTokenSource(App is null ? TimeSpan.FromSeconds(30)
                : App.Services.GetRequiredService<IOptions<TestExecutionOptions>>().Value.ApplicationCleanupTimeout,
                TimeProvider.System);
        var contendersReleased = await fileContenders.JoinAsync(cleanup.Token, failures).ConfigureAwait(false);
        if (contendersReleased && App is { } application)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await application.StopAsync(cleanup.Token).ConfigureAwait(false);
                stopped = true;
            }, failures).ConfigureAwait(false);
        }
        await FunctionalGateResourceLogs.JoinAsync(diagnostics, NodeNames, diagnosticSecrets,
            Root, saveStartupEvidence!, failures).ConfigureAwait(false);
        if (contendersReleased && builder is { } ownedBuilder)
        {
            await ServerFailureObserver.ObserveAsync(() => ownedBuilder.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        if (stopped && started && failures.Count == 0)
        {
            ServerFailureObserver.Observe(() => FunctionalGateStorageOwnership.RequireReleased(this), failures);
            if (failures.Count == 0 && PhysicalFiles is { } physicalFiles)
            {
                ServerFailureObserver.Observe(() => FunctionalGatePhysicalFileAssertions.RequireStopped(this, physicalFiles), failures);
            }
        }
        if (imageSession is { } ownedImage)
        {
            await ServerFailureObserver.ObserveAsync(() => ownedImage.DisposeAsync(
                removeImage: stopped && failures.Count == 0 && !PreserveEvidence).AsTask(), failures).ConfigureAwait(false);
        }
        if (stopped && failures.Count == 0 && !PreserveEvidence)
        {
            ServerFailureObserver.Observe(() =>
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }, failures);
        }
        ReportRetainedRoot(failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void ReportRetainedRoot(List<Exception> failures)
    {
        if (Directory.Exists(Root))
        {
            foreach (var failure in failures.ToArray())
            {
                ServerFailureObserver.Observe(() => failure.Data[FunctionalGateFixtureStartup.FailureRootDataKey] = Root, failures);
            }
            ServerFailureObserver.Observe(() => Console.Error.WriteLine("Original functional gate root retained at " + Root), failures);
        }
    }
}
