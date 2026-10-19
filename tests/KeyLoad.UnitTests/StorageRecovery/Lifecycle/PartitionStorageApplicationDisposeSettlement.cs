using KeyLoad.Server;
using Microsoft.AspNetCore.Builder;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageApplicationDisposeSettlement
{
    internal static async Task CloseAsync(WebApplication? application,
        PartitionStorageApplicationDisposeCapture? captured, Dictionary<string, byte[]>? files,
        long maximumBytes, List<Exception> failures, CancellationToken token)
    {
        Task? originalDispose = null;
        if (application is not null)
        {
            // This is generic DI disposal, with NO preceding app.Stop/node.Stop/registry/producer join.
            await ServerFailureObserver.ObserveAsync(() => originalDispose = application.DisposeAsync().AsTask(),
                failures).ConfigureAwait(false);
        }
        if (captured is null)
        {
            return;
        }
        await ServerFailureObserver.ObserveAsync(() =>
            PartitionStorageApplicationDisposeAssertions.ClosedAsync(captured, originalDispose), failures)
            .ConfigureAwait(false);
        if (originalDispose?.IsCompletedSuccessfully == true && captured.Owner.CanReleaseRoot)
        {
            PartitionStorageApplicationDisposeAssertions.RequireReleased(captured.Directory, failures);
            if (files is not null)
            {
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var after = await PartitionStorageApplicationDisposeFiles.ReadAsync(captured.Directory,
                        maximumBytes, token).ConfigureAwait(false);
                    await PartitionStorageApplicationDisposeFiles.RequireAsync(files, after).ConfigureAwait(false);
                }, failures).ConfigureAwait(false);
            }
        }
    }
}
