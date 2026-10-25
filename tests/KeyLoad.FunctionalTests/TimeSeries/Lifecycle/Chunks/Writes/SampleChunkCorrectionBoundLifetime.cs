using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.TimeSeries;

internal static class SampleChunkCorrectionBoundLifetime
{
    internal static async Task RunAsync(Func<SampleChunkCanonicalFixture, Task> operation)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new SampleChunkCanonicalFixture();
            await ServerFailureObserver.ObserveAsync(() => operation(fixture), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
