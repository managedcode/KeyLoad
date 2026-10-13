namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class CatalogRestoreCancellationRefusal
{
    private const string StagingPrefix = ".keyload-restore-*";

    internal static async Task RunAsync(EventingArtifactFixture fixture,
        CatalogRestoreCancellationAuthority authority, string backup, string target,
        bool existingEmpty, bool afterReconcile, CancellationToken token)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var selected = CancellationTokenSource.CreateLinkedTokenSource(token);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var work = fixture.Source.CreateReadWork(selected.Token);
                if (!afterReconcile)
                { await selected.CancelAsync(); }
                token.ThrowIfCancellationRequested();
                OperationCanceledException? originalFailure = null;
                try
                {
                    authority.Restore(fixture, backup, target, work,
                        afterReconcile ? selected : null, selected.Token);
                }
                catch (OperationCanceledException failure) { originalFailure = failure; }
                await Assert.That(token.IsCancellationRequested).IsFalse();
                await Assert.That(selected.IsCancellationRequested).IsTrue();
                await Assert.That(originalFailure).IsNotNull();
                await Assert.That(originalFailure!.CancellationToken).IsEqualTo(selected.Token);
                await Assert.That(Directory.Exists(target)).IsEqualTo(existingEmpty);
                if (existingEmpty)
                { await Assert.That(Directory.EnumerateFileSystemEntries(target).Any()).IsFalse(); }
                await Assert.That(Directory.EnumerateDirectories(fixture.Root, StagingPrefix).Any()).IsFalse();
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
