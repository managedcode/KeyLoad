using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Diagnostics.Features.ResourceExecution;
using KeyLoad.IntegrationTests.Features.StorageReplication;
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
        // Publish the one original cleanup task before a execution cancellation callback can reenter.
        await Task.Yield();
        var failures = new List<Exception>();
        var stopped = App is null;
        using var cleanup = new CancellationTokenSource(App is null ? TimeSpan.FromSeconds(30)
                : App.Services.GetRequiredService<IOptions<TestExecutionOptions>>().Value.ApplicationCleanupTimeout,
                TimeProvider.System);
        var contendersReleased = await fileContenders.JoinAsync(failures, cleanup.Token).ConfigureAwait(false);
        if (contendersReleased)
        {
            stopped = await StopApplicationAsync(failures, cleanup.Token).ConfigureAwait(false);
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

    private async Task<bool> StopApplicationAsync(List<Exception> failures, CancellationToken token)
    {
        if (App is not { } application)
        {
            return true;
        }
        ProcessEvidence[] observed = [];
        var stopped = false;
        if (processEvidenceDirectory is { } evidenceOutput)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
                observed = await ClusterFixtureApplicationStartup.CaptureAsync(application, NodeNames, evidenceOutput,
                    "execution-process-before-shutdown.json", token).ConfigureAwait(false), failures).ConfigureAwait(false);
        }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await application.StopAsync(token).ConfigureAwait(false);
            stopped = true;
        }, failures).ConfigureAwait(false);
        if (stopped && observed.Length == NodeNames.Length && processEvidenceDirectory is { } output)
        {
            await ServerFailureObserver.ObserveAsync(
                () => ClusterFixtureApplicationShutdown.WriteJoinedAsync(application, observed, output, token),
                failures).ConfigureAwait(false);
        }
        return stopped;
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
