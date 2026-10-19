using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class BackupUnsafeDestinationFlow
{
    internal static async Task RunAsync(bool ancestorLink, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new BackupCutFixture(checkpoint: true);
            await ServerFailureObserver.ObserveAsync(
                () => BackupUnsafeDestinationTrial.RunAsync(fixture, ancestorLink, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
