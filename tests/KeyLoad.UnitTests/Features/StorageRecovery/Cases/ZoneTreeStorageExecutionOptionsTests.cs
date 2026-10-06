using System.Buffers.Binary;
using System.Text;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class ZoneTreeStorageExecutionOptionsTests
{
    private const string DirectoryPrefix = "keyload-storage-execution-";
    private const string ConfiguredSnapshot = "configured.snapshot";
    private const string DefaultSnapshot = "default.snapshot";
    private static readonly byte[] Value = Encoding.UTF8.GetBytes("v");

    [Test]
    public async Task ConfiguredRangeAndReadCutBudgetsBoundActualStoredRecords()
    {
        var settings = new ZoneTreeStorageExecutionOptions
        {
            MaximumRangeRecords = 3,
            MaximumRangeWorkBytes = 6,
            MaximumReadCutRecords = 2,
            MaximumReadCutExaminedBytes = 32,
            MaximumReadCutElapsed = TimeSpan.FromSeconds(1)
        };
        using var fixture = new StorageExecutionFixture(settings);
        Seed(fixture.Store);
        var delivered = 0;
        var bounded = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.Read(view =>
            view.VisitRange([], 3, (_, _) => { delivered++; return true; })));
        await Assert.That(bounded.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(delivered).IsEqualTo(2);
        var invalid = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.Read(view =>
            fixture.Store.CaptureNativeReadCut(view, new(3, 32, TimeSpan.FromSeconds(1)), default)));
        await Assert.That(invalid.Code).IsEqualTo(ErrorCode.Validation);

        using (var cut = fixture.Store.Read(view => fixture.Store.CaptureNativeReadCut(view,
            new(2, 32, TimeSpan.FromSeconds(1)), default)))
        {
            delivered = 0;
            var exhausted = Assert.ThrowsExactly<KeyLoadException>(() => cut.VisitPrefix([], (_, _) =>
            { delivered++; return true; }));
            await Assert.That(exhausted.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            await Assert.That(delivered).IsEqualTo(2);
        }

        using var healthy = fixture.Store.Read(view => fixture.Store.CaptureNativeReadCut(view,
            new(2, 32, TimeSpan.FromSeconds(1)), default));
        await Assert.That(healthy.VisitPrefix([], static (_, _) => false).Records).IsEqualTo(1);
    }

    [Test]
    public async Task ExplicitCanonicalDefaultOverridesRemainExactAgainstDifferentCentralPolicy()
    {
        var options = UnitExecutionOptions.StorageExecution(new() { MaxFrameBytes = 1, MaxSnapshotBytes = 1 });
        var path = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString("N"));
        var descriptor = new ZoneTreeStoreOptions(path)
        {
            MaxFrameBytes = ZoneTreeStorageExecutionOptions.DefaultMaxFrameBytes,
            MaxSnapshotBytes = ZoneTreeStorageExecutionOptions.DefaultMaxSnapshotBytes
        };
        var resolved = descriptor.ResolveExecutionOptions(options);
        await Assert.That(resolved.MaxFrameBytes).IsEqualTo(ZoneTreeStorageExecutionOptions.DefaultMaxFrameBytes);
        await Assert.That(resolved.MaxSnapshotBytes).IsEqualTo(ZoneTreeStorageExecutionOptions.DefaultMaxSnapshotBytes);
        var inherited = new ZoneTreeStoreOptions(path).ResolveExecutionOptions(options);
        await Assert.That(inherited.MaxFrameBytes).IsEqualTo(1);
        await Assert.That(inherited.MaxSnapshotBytes).IsEqualTo(1L);
        try
        {
            using (var store = new ZoneTreeStore(descriptor, options, UnitExecutionOptions.PointCacheExecution()))
            {
                Seed(store);
            }
            using var reopened = new ZoneTreeStore(descriptor, options, UnitExecutionOptions.PointCacheExecution());
            await Assert.That(reopened.Read(view => view.ReadOwnedValue(Encoding.UTF8.GetBytes("a"))!))
                .IsEquivalentTo(Value);
        }
        finally
        {
            if (Directory.Exists(path))
            { Directory.Delete(path, recursive: true); }
        }
    }

    [Test]
    public async Task ConfiguredCheckpointBatchingChangesNativeFramesAndPreservesReopenedData()
    {
        using var fixture = new StorageExecutionFixture(new() { CheckpointBatchRecords = 1 });
        Seed(fixture.Store);
        var configured = Path.Combine(fixture.DirectoryPath, ConfiguredSnapshot);
        fixture.Store.CreateSnapshot(configured);
        await Assert.That(CountDataFrames(configured)).IsEqualTo(3);
        fixture.Reopen(new());
        var defaults = Path.Combine(fixture.DirectoryPath, DefaultSnapshot);
        fixture.Store.CreateSnapshot(defaults);
        await Assert.That(CountDataFrames(defaults)).IsEqualTo(1);
        await Assert.That(fixture.Store.Read(view => view.Scan([], 3).Records.Length)).IsEqualTo(3);
    }

    [Test]
    public async Task InvalidNativePolicyFailsBeforeDirectoryOwnershipIsCreated()
    {
        var path = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString("N"));
        var invalid = Options.Create(new ZoneTreeStorageExecutionOptions { CheckpointBatchRecords = 0 });
        Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            using var store = new ZoneTreeStore(new(path), invalid, UnitExecutionOptions.PointCacheExecution());
        });
        await Assert.That(Directory.Exists(path)).IsFalse();
    }

    private static void Seed(ZoneTreeStore store) => store.Commit((tx, _) =>
    {
        foreach (var name in new[] { "a", "b", "c" })
        { tx.Put(Encoding.UTF8.GetBytes(name), Value); }
        return true;
    });

    private static int CountDataFrames(string path)
    {
        var frames = 0;
        using var input = File.OpenRead(path);
        var header = new byte[ZoneTreePersistenceFormat.HeaderLength];
        while (input.Position < input.Length)
        {
            input.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt64LittleEndian(header) == ZoneTreePersistenceFormat.CheckpointDataMagic)
            { frames++; }
            input.Position += BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(ZoneTreePersistenceFormat.PayloadLengthOffset));
        }
        return frames;
    }

    private sealed class StorageExecutionFixture : IDisposable
    {
        internal string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString("N"));
        internal ZoneTreeStore Store { get; private set; }

        internal StorageExecutionFixture(ZoneTreeStorageExecutionOptions settings)
            => Store = new(new(DirectoryPath), UnitExecutionOptions.StorageExecution(settings), UnitExecutionOptions.PointCacheExecution());

        internal void Reopen(ZoneTreeStorageExecutionOptions settings)
        {
            Store.Dispose();
            Store = new(new(DirectoryPath), UnitExecutionOptions.StorageExecution(settings), UnitExecutionOptions.PointCacheExecution());
        }

        public void Dispose()
        {
            Store.Dispose();
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
