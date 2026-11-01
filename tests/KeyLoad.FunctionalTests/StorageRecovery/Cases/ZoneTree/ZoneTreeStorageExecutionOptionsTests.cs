using System.Text;
using KeyLoad.FunctionalTests.ResourceExecution;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class ZoneTreeStorageExecutionOptionsTests
{
    private const string DirectoryPrefix = "keyload-storage-execution-";
    private static readonly byte[] Value = Encoding.UTF8.GetBytes("v");
    private static readonly byte[] Prefix = Encoding.UTF8.GetBytes("k");

    [Test]
    public async Task ConfiguredRangeAndReadCutBudgetsBoundActualStoredRecords()
    {
        var settings = new ZoneTreeStorageExecutionOptions
        {
            MaximumRangeRecords = 3,
            MaximumRangeWorkBytes = 8,
            MaximumReadCutRecords = 2,
            MaximumReadCutExaminedBytes = 32,
            MaximumReadCutElapsed = TimeSpan.FromSeconds(1)
        };
        var clock = new ControlledReadClock(new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var fixture = new StorageExecutionFixture(settings, clock);
        Seed(fixture.Store);
        var delivered = 0;
        var bounded = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.Read(view =>
            view.VisitRange(Prefix, 3, (_, _) => { delivered++; return true; })));
        await Assert.That(bounded.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(delivered).IsEqualTo(2);
        var invalid = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.Read(view =>
            fixture.Store.CaptureReadCut(view, new(3, 32, TimeSpan.FromSeconds(1)), default)));
        await Assert.That(invalid.Code).IsEqualTo(ErrorCode.Validation);

        using (var cut = fixture.Store.Read(view => fixture.Store.CaptureReadCut(view,
            new(2, 32, TimeSpan.FromSeconds(1)), default)))
        {
            delivered = 0;
            var exhausted = Assert.ThrowsExactly<KeyLoadException>(() => cut.VisitPrefix(Prefix, (_, _) =>
            {
                delivered++;
                return true;
            }));
            await Assert.That(exhausted.Code).IsEqualTo(ErrorCode.BudgetExceeded);
            await Assert.That(delivered).IsEqualTo(2);
        }

        using var healthy = fixture.Store.Read(view => fixture.Store.CaptureReadCut(view,
            new(2, 32, TimeSpan.FromSeconds(1)), default));
        await Assert.That(healthy.VisitPrefix(Prefix, static (_, _) => false).Records).IsEqualTo(1);
    }

    [Test]
    public async Task InvalidPolicyFailsBeforeDirectoryOwnershipIsCreated()
    {
        var path = Path.Combine(AppContext.BaseDirectory, DirectoryPrefix + Guid.NewGuid().ToString("N"));
        var invalid = Options.Create(new ZoneTreeStorageExecutionOptions { MaximumRangeRecords = 0 });
        Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            using var store = new ZoneTreeStore(new(path), invalid);
        });
        await Assert.That(Directory.Exists(path)).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(4097)]
    public async Task InvalidStreamBufferFailsBeforeDirectoryOwnershipIsCreated(int bufferBytes)
    {
        var path = Path.Combine(AppContext.BaseDirectory, DirectoryPrefix + Guid.NewGuid().ToString("N"));
        var invalid = Options.Create(new ZoneTreeStorageExecutionOptions { StreamBufferBytes = bufferBytes });
        Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            using var store = new ZoneTreeStore(new(path), invalid);
        });
        await Assert.That(Directory.Exists(path)).IsFalse();
    }

    private static void Seed(ZoneTreeStore store) => store.Commit((tx, _) =>
    {
        foreach (var name in new[] { "ka", "kb", "kc" })
        {
            tx.Put(Encoding.UTF8.GetBytes(name), Value);
        }
        return true;
    });

    private sealed class StorageExecutionFixture : IDisposable
    {
        private readonly TimeProvider clock;
        internal string DirectoryPath { get; } = Path.Combine(AppContext.BaseDirectory, DirectoryPrefix + Guid.NewGuid().ToString("N"));
        internal ZoneTreeStore Store
        {
            get; private set;
        }

        internal StorageExecutionFixture(ZoneTreeStorageExecutionOptions settings, TimeProvider? timeProvider = null)
        {
            clock = timeProvider ?? TimeProvider.System;
            Store = new(new(DirectoryPath), UnitExecutionOptions.StorageExecution(settings), clock);
        }

        public void Dispose()
        {
            Store.Dispose();
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
