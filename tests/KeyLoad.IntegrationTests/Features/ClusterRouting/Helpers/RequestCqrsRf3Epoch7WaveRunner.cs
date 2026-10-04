using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsRf3Epoch7WaveRunner
{
    internal static async Task RunAsync(string dataRoot, IReadOnlyDictionary<string, string> images,
        bool configureCohort, bool requireHealthy, Func<RequestCqrsRf3Wave, Task> action,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        RequestCqrsRf3Wave? wave = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await RequestCqrsRf3Wave.StartAsync(dataRoot, images, configureCohort, requireHealthy,
                cancellationToken).ConfigureAwait(false);
            await action(wave).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (wave is not null)
        { await ServerFailureObserver.ObserveAsync(wave.StopAsync, failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
