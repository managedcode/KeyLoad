using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsRf3Epoch7WaveRunner
{
    internal static Task RunAsync(string dataRoot, IReadOnlyDictionary<string, string> images,
        bool configureCohort, bool requireHealthy, Func<RequestCqrsRf3Wave, Task> action,
        CancellationToken cancellationToken, RequestCqrsProbeFixture? controls = null)
        => RunCoreAsync(dataRoot, images, configureCohort, requireHealthy, action, controls, cancellationToken);

    private static async Task RunCoreAsync(string dataRoot, IReadOnlyDictionary<string, string> images,
        bool configureCohort, bool requireHealthy, Func<RequestCqrsRf3Wave, Task> action,
        RequestCqrsProbeFixture? controls, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        RequestCqrsRf3Wave? wave = null;
        var diagnosticsWaveId = Guid.NewGuid();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await RequestCqrsRf3Wave.StartAsync(dataRoot, images, configureCohort,
                requireHealthy, diagnosticsWaveId, cancellationToken, controls: controls).ConfigureAwait(false);
            await action(wave).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (wave is not null)
        {
            var originalFailure = failures.FirstOrDefault();
            await ServerFailureObserver.ObserveAsync(wave.StopAsync, failures).ConfigureAwait(false);
            if (originalFailure is not null)
            { ServerFailureObserver.Observe(() => wave.SaveFailureEvidence(originalFailure), failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
