using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class DueRf3WaveLifecycle
{
    internal static async Task<T> RunAsync<T>(string dataRoot, IReadOnlyDictionary<string, string> images,
        Func<RequestCqrsRf3Wave, Task<T>> action, CancellationToken cancellationToken,
        RequestCqrsLifecycleEvidence? lifecycle = null)
    {
        var failures = new List<Exception>();
        RequestCqrsRf3Wave? wave = null;
        T? result = default;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                wave = await RequestCqrsRf3WaveStartup.StartAsync(dataRoot, images, false, true, null,
                    Guid.NewGuid(), cancellationToken, lifecycleEvidence: lifecycle)
                    .ConfigureAwait(false);
                lifecycle?.SetWaveToken(cancellationToken);
                result = await action(wave).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            lifecycle?.RecordFirstFailureIfAny(failures);
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
