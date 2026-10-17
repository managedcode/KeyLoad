using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsRf3Epoch7WaveRunner
{
    internal static Task RunAsync(string dataRoot, IReadOnlyDictionary<string, string> images,
        bool configureCohort, bool requireHealthy, Func<RequestCqrsRf3Wave, Task> action,
        CancellationToken cancellationToken, RequestCqrsProbeFixture? controls = null,
        LocalRf3ImageTestSession? localImageSession = null)
        => RunCoreAsync(dataRoot, images, configureCohort, requireHealthy, action, controls, null, localImageSession, cancellationToken);

    internal static Task RunObservedAsync(string dataRoot, IReadOnlyDictionary<string, string> images,
        bool configureCohort, bool requireHealthy, Func<RequestCqrsRf3Wave, Task> action,
        RequestCqrsRf3ObservedWaveScope scope, CancellationToken cancellationToken)
        => RunCoreAsync(dataRoot, images, configureCohort, requireHealthy, action, null, scope, null, cancellationToken);

    private static async Task RunCoreAsync(string dataRoot, IReadOnlyDictionary<string, string> images,
        bool configureCohort, bool requireHealthy, Func<RequestCqrsRf3Wave, Task> action,
        RequestCqrsProbeFixture? controls, RequestCqrsRf3ObservedWaveScope? scope, LocalRf3ImageTestSession? localImageSession,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        RequestCqrsRf3Wave? wave = null;
        var diagnosticsWaveId = Guid.NewGuid();
        var startupAttempted = false;
        var waveStopped = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            startupAttempted = true;
            wave = await RequestCqrsRf3WaveStartup.StartAsync(dataRoot, images, configureCohort,
                requireHealthy, null, diagnosticsWaveId, cancellationToken, controls: controls,
                physicalShardOverrideNode: scope?.PhysicalShardOverrideNode,
                physicalShardOverrideId: scope?.PhysicalShardOverrideId,
                lifecycleEvidence: scope?.Lifecycle, selection: localImageSession?.Selection).ConfigureAwait(false);
            await action(wave).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        scope?.Lifecycle.RecordFirstFailureIfAny(failures);
        if (wave is not null)
        {
            var originalFailure = failures.FirstOrDefault();
            var beforeStop = failures.Count;
            await ServerFailureObserver.ObserveAsync(wave.StopAsync, failures).ConfigureAwait(false);
            waveStopped = failures.Count == beforeStop;
            if (originalFailure is not null)
            {
                ServerFailureObserver.Observe(() => wave.SaveFailureEvidence(originalFailure), failures);
            }
        }
        if (localImageSession is not null)
        {
            await ClusterFixtureLocalImageLifecycle.JoinAsync(localImageSession, dataRoot, waveStopped,
                !startupAttempted, failures).ConfigureAwait(false);
        }
        scope?.Lifecycle.RecordTerminal();
        if (scope is not null && failures.Count > 0)
        {
            scope.Lifecycle.ThrowWithContext(failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
