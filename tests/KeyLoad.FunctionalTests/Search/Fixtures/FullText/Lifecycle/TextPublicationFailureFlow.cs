using KeyLoad.Query.Search;
using KeyLoad.Server;
using KeyLoad.Server.Search;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;
using TUnit.Assertions.Enums;
using ZoneTree;

namespace KeyLoad.FunctionalTests.Search;

internal static class TextPublicationFailureFlow
{
    internal const string Collection = "publication-native-close";
    internal const string Payload = "{\"text\":\"needle\"}";

    internal static async Task RefuseAsync(TestDatabase database, string directory, CancellationToken token)
    {
        var options = UnitTextOptions.Execution();
        var resources = new TextResourceOwnership(options);
        var root = TextFiles.InitializeRoot(directory, database.Store.Identity.NodeId, options, resources);
        var state = new TextProjectionState(options);
        var gate = new TextProjectionPhysicalGate();
        var scope = Scope(database);
        var lifecycle = new RuntimeTextProjectionLifecycle(root, scope.NodeId, state, gate, null, options, resources);
        var reservation = state.Reserve(scope);
        var maximumFiles = options.Value.MaximumFiles;
        var failures = new List<Exception>();
        TextGeneration? generation = null;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var budget = new ReadExecutionBudget(cancellationToken: token);
                generation = lifecycle.CreateGeneration(reservation.Slot, scope, budget);
                Stage(database, generation);
                await Assert.That(generation.Index.SimpleSearch([TextHash.Sha256("needle")]))
                    .IsEquivalentTo(new ulong[] { 1, 2 }, CollectionOrdering.Matching);
                var observation = ArmFailure(root, generation, options, maximumFiles);
                var failure = Assert.ThrowsExactly<KeyLoadException>(() => lifecycle.Publish(generation, budget));
                await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
                await Assert.That(observation.Failed).IsTrue();
                await Assert.That(observation.PendingRecords).IsGreaterThan(0L);
                await Assert.That(generation.Published).IsFalse();
                await Assert.That(generation.CurrentIndex).IsNull();
                await Assert.That(File.Exists(Path.Combine(generation.Path, TextProtocol.ManifestFile))).IsFalse();
                await Assert.That(File.Exists(Path.Combine(generation.Path, TextProtocol.PendingManifestFile))).IsFalse();
            }, failures);
        }
        finally
        {
            options.Value.MaximumFiles = maximumFiles;
            options.Value.Validate();
            ServerFailureObserver.Observe(() => generation?.DisposeIndex(), failures);
            ServerFailureObserver.Observe(() => state.FailAcquire(reservation, preserveOwner: true), failures);
            ServerFailureObserver.Observe(state.BeginShutdown, failures);
            ServerFailureObserver.Observe(() => lifecycle.Shutdown(state.SnapshotSlots()), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(Directory.EnumerateDirectories(root)).IsEmpty();
    }

    private static void Stage(TestDatabase database, TextGeneration generation)
    {
        var ordinal = 0UL;
        var hash = TextHash.Sha256("needle");
        foreach (var id in new[] { "a", "b" })
        {
            var reference = database.ExpectedReference(new(database.Partition, Collection, id));
            var document = database.Database.GetDocument("root", reference)!;
            generation.Corpus.AddRecord(document.Reference, document.Revision);
            generation.Index.UpsertRecord(hash, ++ordinal, 0);
            generation.Corpus.ObservePosting(hash, ordinal, 0);
        }
    }

    internal static async Task RefuseDamagedPostingsAsync(TestDatabase database, string directory, CancellationToken token)
    {
        await RefuseDamagedPostingsAsync(database, directory, substitute: false, token);
        await RefuseDamagedPostingsAsync(database, directory, substitute: true, token);
    }

    private static async Task RefuseDamagedPostingsAsync(TestDatabase database, string directory, bool substitute,
        CancellationToken token)
    {
        var options = UnitTextOptions.Execution();
        var resources = new TextResourceOwnership(options);
        var scope = Scope(database);
        var root = TextFiles.InitializeRoot(directory, scope.NodeId, options, resources);
        var state = new TextProjectionState(options);
        var lifecycle = new RuntimeTextProjectionLifecycle(root, scope.NodeId, state, new(), null, options, resources);
        var reservation = state.Reserve(scope);
        var work = new ReadExecutionBudget(cancellationToken: token);
        TextGeneration? generation = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                generation = lifecycle.CreateGeneration(reservation.Slot, scope, work);
                Stage(database, generation);
                var hash = TextHash.Sha256("needle");
                generation.Index.DeleteRecord(hash, 1, 0);
                if (substitute)
                {
                    generation.Index.UpsertRecord(hash, 1, hash);
                }
                var failure = Assert.ThrowsExactly<KeyLoadException>(() => lifecycle.Publish(generation, work));
                await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
                await Assert.That(generation.CurrentIndex).IsNull();
                await Assert.That(generation.Published).IsFalse();
                await Assert.That(File.Exists(Path.Combine(generation.Path, TextProtocol.ManifestFile))).IsFalse();
            }, failures);
        }
        finally
        {
            ServerFailureObserver.Observe(() => generation?.DisposeIndex(), failures);
            ServerFailureObserver.Observe(() => state.FailAcquire(reservation, preserveOwner: true), failures);
            ServerFailureObserver.Observe(state.BeginShutdown, failures);
            ServerFailureObserver.Observe(() => lifecycle.Shutdown(state.SnapshotSlots()), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(Directory.EnumerateDirectories(root)).IsEmpty();
    }

    private static NativeFailure ArmFailure(string root, TextGeneration generation,
        IOptions<TextExecutionOptions> options, int maximumFiles)
    {
        var observation = new NativeFailure();
        generation.Index.Maintainer1.ThresholdForMergeOperationStart = int.MaxValue;
        generation.Index.Maintainer1.MaximumReadOnlySegmentCount = int.MaxValue;
        generation.Index.ZoneTree1.Maintenance.OnMergeOperationEnded += (native, result) =>
        {
            if (result == MergeResult.FAILURE)
            {
                options.Value.MaximumFiles = maximumFiles;
                observation.Observe(native.InMemoryRecordCount);
            }
        };
        var physical = TextFileIO.MeasureRegularFiles(root, maximumFiles, options.Value.MaximumDiskBytes, options);
        options.Value.MaximumFiles = checked(physical.Files + 1);
        options.Value.Validate();
        return observation;
    }

    internal static async Task ColdPostingsAsync(string root, Guid node, IOptions<TextExecutionOptions> options,
        TextResourceOwnership resources, CancellationToken token)
    {
        var generation = Directory.EnumerateDirectories(root).Single();
        var leaf = Path.GetFileName(generation);
        using var index = RuntimeTextIndex.Open(Path.Combine(generation, TextProtocol.RuntimeDirectoryCore),
            new TextFileStreamProvider(root, leaf, node, options, resources, allowReadOnlyInventory: true),
            options, ZoneTree.Options.WriteAheadLogMode.None);
        index.IsReadOnly = true;
        index.WaitForBackgroundThreads();
        await Assert.That(index.ZoneTree1.Maintenance.InMemoryRecordCount).IsEqualTo(0L);
        await Assert.That(index.SimpleSearch([TextHash.Sha256("needle")], cancellationToken: token))
            .IsEquivalentTo(new ulong[] { 1, 2 }, CollectionOrdering.Matching);
    }

    private static TextProjectionScope Scope(TestDatabase database)
    {
        var identity = database.Store.Identity;
        var principal = database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal("root")))!;
        var resource = database.Store.Read(view => view.GetRecord<ResourceDefinition>(KeySpace.Resource(
            database.Partition.TenantId, database.Partition.DatabaseId, Collection)))!;
        return new(identity.NodeId, identity.Incarnation, identity.ReadGeneration, database.Store.Position,
            database.Partition, Collection, "/text", principal.Id, principal.PolicyEpoch, resource.SchemaVersion);
    }

    private sealed class NativeFailure
    {
        private int failed;
        private long pendingRecords;
        internal bool Failed => Volatile.Read(ref failed) != 0;
        internal long PendingRecords => Interlocked.Read(ref pendingRecords);
        internal void Observe(long records)
        {
            Interlocked.Exchange(ref pendingRecords, records);
            Volatile.Write(ref failed, 1);
        }
    }
}
