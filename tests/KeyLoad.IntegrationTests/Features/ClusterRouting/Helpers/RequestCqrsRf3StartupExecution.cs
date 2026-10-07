using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsRf3StartupExecution
{
    private const string MissingWave = "The C1 Aspire wave did not transfer its owned resources.";

    internal static async Task<RequestCqrsRf3Wave> RunAsync(Func<RequestCqrsRf3WaveStartup> createStartup)
    {
        var startup = createStartup();
        var failures = new List<Exception>();
        RequestCqrsRf3Wave? wave = null;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                wave = await startup.RunAsync().ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            try
            { await startup.DisposeAsync().ConfigureAwait(false); }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
            { failures.Add(failure); }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
            { failures.Add(failure); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return wave ?? throw new InvalidOperationException(MissingWave);
    }
}
