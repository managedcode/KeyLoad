using System.Text;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class ZoneTreeMaintenancePolicyTests
{
    private const string DirectoryPrefix = "keyload-maintenance-policy-";
    private static readonly byte[] Value = Encoding.UTF8.GetBytes("v");

    [Test]
    [Arguments(0L, TimeSpan.TicksPerMinute)]
    [Arguments(-1L, TimeSpan.TicksPerMinute)]
    [Arguments(1L, TimeSpan.TicksPerMinute)]
    [Arguments(42_949_672_950_000L, TimeSpan.TicksPerMinute)]
    [Arguments(TimeSpan.TicksPerSecond, -1L)]
    [Arguments(TimeSpan.TicksPerSecond, 42_949_672_950_000L)]
    public async Task InvalidMaintenanceDurationsRejectBeforeOwnershipThenHealthyCommitAndColdReopen(
        long intervalTicks, long lifetimeTicks)
    {
        var path = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString("N"));
        var invalid = Options.Create(new ZoneTreeStorageExecutionOptions
        {
            NativeMaintenanceInterval = TimeSpan.FromTicks(intervalTicks),
            NativeBlockCacheLifetime = TimeSpan.FromTicks(lifetimeTicks)
        });
        Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            using var rejected = new ZoneTreeStore(new(path), invalid, UnitExecutionOptions.PointCacheExecution());
        });
        await Assert.That(Directory.Exists(path)).IsFalse();
        try
        {
            long position;
            Guid incarnation;
            using (var store = new ZoneTreeStore(new(path), UnitExecutionOptions.StorageExecution(),
                UnitExecutionOptions.PointCacheExecution()))
            {
                Seed(store);
                position = store.Position;
                incarnation = store.Identity.Incarnation;
            }
            using (var reopened = new ZoneTreeStore(new(path), UnitExecutionOptions.StorageExecution(),
                UnitExecutionOptions.PointCacheExecution()))
            {
                await Assert.That(reopened.Position).IsEqualTo(position);
                await Assert.That(reopened.Identity.Incarnation).IsEqualTo(incarnation);
                foreach (var name in new[] { "a", "b", "c" })
                {
                    await Assert.That(reopened.Read(view => view.ReadOwnedValue(Encoding.UTF8.GetBytes(name))!))
                        .IsEquivalentTo(Value);
                }
                reopened.Commit((tx, _) => { tx.Put(Encoding.UTF8.GetBytes("d"), Value); return true; });
                await Assert.That(reopened.Position).IsGreaterThan(position);
            }
            using var settled = new ZoneTreeStore(new(path), UnitExecutionOptions.StorageExecution(),
                UnitExecutionOptions.PointCacheExecution());
            await Assert.That(settled.Read(view => view.Scan([], 4).Records.Length)).IsEqualTo(4);
            foreach (var name in new[] { "a", "b", "c", "d" })
            {
                await Assert.That(settled.Read(view => view.ReadOwnedValue(Encoding.UTF8.GetBytes(name))!))
                    .IsEquivalentTo(Value);
            }
        }
        finally
        {
            if (Directory.Exists(path))
            { Directory.Delete(path, recursive: true); }
        }
    }

    private static void Seed(ZoneTreeStore store) => store.Commit((tx, _) =>
    {
        foreach (var name in new[] { "a", "b", "c" })
        { tx.Put(Encoding.UTF8.GetBytes(name), Value); }
        return true;
    });
}
