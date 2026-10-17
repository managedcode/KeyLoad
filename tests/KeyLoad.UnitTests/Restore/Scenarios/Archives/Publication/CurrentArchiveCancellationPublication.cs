using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class CurrentArchiveCancellationPublication
{
    private const string CatalogSuffix = "-catalog-cancellation";
    private const string MetadataValue = "current-native-archive-cancellation";

    internal static async Task RunAsync(NativeBackupCutFixture fixture, CancellationToken token)
    {
        var destination = fixture.Destination + CatalogSuffix;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = new ZoneTreeStore(new(fixture.Source), UnitExecutionOptions.StorageExecution(),
                UnitExecutionOptions.PointCacheExecution());
            await ServerFailureObserver.ObserveAsync(() => CompleteAsync(store, destination, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ReadCancelledAsync(string directory)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cancelled = new CancellationTokenSource();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await cancelled.CancelAsync();
                var metadata = Assert.ThrowsExactly<OperationCanceledException>(() => ZoneTreeStore.ReadVerifiedCatalogBackup(
                    directory, UnitExecutionOptions.StorageExecution(), cancelled.Token));
                await Assert.That(metadata.CancellationToken).IsEqualTo(cancelled.Token);
                var invoked = false;
                var view = Assert.ThrowsExactly<OperationCanceledException>(() => ZoneTreeStore.ReadVerifiedCatalogBackup(
                    directory, UnitExecutionOptions.StorageExecution(), (_, _, _, _) =>
                    {
                        invoked = true;
                        return true;
                    },
                    cancelled.Token));
                await Assert.That(view.CancellationToken).IsEqualTo(cancelled.Token);
                await Assert.That(invoked).IsFalse();
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CompleteAsync(ZoneTreeStore store, string destination, CancellationToken token)
    {
        var image = EventingArtifactState.FullBytes(store);
        var identity = NativeSerialization.Serialize(store.Identity);
        var position = store.Position;
        var metadata = NativeSerialization.Serialize(MetadataValue);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => CaptureAndReadAsync(store, destination, metadata, position,
            identity, image, token), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(store.Position).IsEqualTo(position);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(NativeSerialization.Serialize(store.Identity).AsSpan().SequenceEqual(identity)).IsTrue();
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(EventingArtifactState.FullBytes(store))
                .IsEquivalentTo(image, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CaptureAndReadAsync(ZoneTreeStore store, string destination, byte[] metadata,
        long position, byte[] identity, string[] image, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cancelled = new CancellationTokenSource();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await cancelled.CancelAsync();
                var invoked = false;
                var original = Assert.ThrowsExactly<OperationCanceledException>(() => store.CreateCatalogBackup(destination,
                    (_, _, _) =>
                    {
                        invoked = true;
                        return metadata;
                    }, cancelled.Token));
                await Assert.That(original.CancellationToken).IsEqualTo(cancelled.Token);
                await Assert.That(invoked).IsFalse();
                await Assert.That(Directory.Exists(destination)).IsFalse();
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        var capture = store.CreateCatalogBackup(destination, (_, _, _) => metadata, token);
        await Assert.That(capture.Position).IsEqualTo(position);
        await Assert.That(capture.Metadata.Span.SequenceEqual(metadata)).IsTrue();
        var archive = await NativeBackupJournalRepairAssertions.ArchiveAsync(destination, token);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await ReadCancelledAsync(destination);
            var read = ZoneTreeStore.ReadVerifiedCatalogBackup(destination, UnitExecutionOptions.StorageExecution(),
                (view, originalIdentity, originalPosition, originalMetadata) =>
                {
                    var page = view.Scan([], 128);
                    if (page.HasMore)
                    {
                        throw new InvalidOperationException("The current archive fixture exceeded its complete image bound.");
                    }
                    return (Identity: NativeSerialization.Serialize(originalIdentity), Position: originalPosition,
                        Metadata: originalMetadata.ToArray(), Rows: page.Records.Select(row =>
                            Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray());
                }, token);
            await Assert.That(read.Identity.AsSpan().SequenceEqual(identity)).IsTrue();
            await Assert.That(read.Position).IsEqualTo(position);
            await Assert.That(read.Metadata.AsSpan().SequenceEqual(metadata)).IsTrue();
            await Assert.That(read.Rows).IsEquivalentTo(image, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() =>
            CurrentArchiveCancellationFlow.ArchiveAsync(archive, destination, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(EventingArtifactState.FullBytes(store))
            .IsEquivalentTo(image, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
