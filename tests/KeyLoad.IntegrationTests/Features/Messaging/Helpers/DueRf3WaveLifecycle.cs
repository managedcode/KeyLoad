using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class DueRf3WaveLifecycle
{
    internal static async Task<T> RunAsync<T>(string dataRoot, IReadOnlyDictionary<string, string> images,
        Func<RequestCqrsRf3Wave, Task<T>> action, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        RequestCqrsRf3Wave? wave = null;
        T? result = default;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                wave = await RequestCqrsRf3Wave.StartAsync(dataRoot, images, false, true, cancellationToken)
                    .ConfigureAwait(false);
                result = await action(wave).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            if (wave is not null)
            { await CompleteWaveAsync(wave, failures).ConfigureAwait(false); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result!;
    }

    private static async Task CompleteWaveAsync(RequestCqrsRf3Wave wave, List<Exception> failures)
    {
        await ServerFailureObserver.ObserveAsync(wave.StopAsync, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => wave.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
    }
}
