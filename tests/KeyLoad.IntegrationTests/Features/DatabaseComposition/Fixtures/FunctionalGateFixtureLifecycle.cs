using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
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
        if (App is { } application)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await application.StopAsync(cleanup.Token).ConfigureAwait(false);
                stopped = true;
            }, failures).ConfigureAwait(false);
        }
        if (diagnostics is { } logs)
        {
            try
            {
                await diagnostics.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception failure) when (KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            ServerFailureObserver.Observe(() => FunctionalGateResourceLogs.Save(
                logs, NodeNames, diagnosticSecrets, Root, saveStartupEvidence!), failures);
        }
        if (builder is { } ownedBuilder)
        {
            await ServerFailureObserver.ObserveAsync(() => ownedBuilder.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        if (stopped && started && failures.Count == 0)
        {
            ServerFailureObserver.Observe(RequireReleasedStorage, failures);
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
                ServerFailureObserver.Observe(() => failure.Data[FailureRootDataKey] = Root, failures);
            }
            ServerFailureObserver.Observe(() => Console.Error.WriteLine("Original functional gate root retained at " + Root), failures);
        }
    }

    private void RequireReleasedStorage()
    {
        foreach (var node in NodeNames)
        {
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(Root, node, "node.owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(Root, node, "database", "owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(Root, node, "replica", "owner.lock"));
        }
    }
}
