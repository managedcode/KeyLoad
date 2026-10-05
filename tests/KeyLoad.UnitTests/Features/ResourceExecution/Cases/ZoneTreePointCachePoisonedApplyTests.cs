using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ZoneTreePointCachePoisonedApplyTests
{
    private static readonly byte[] KeyA = "cache/a"u8.ToArray();
    private static readonly byte[] ValueA = [10, 11];

    [Test]
    public async Task PoisonedApplyCannotServeHitsAndColdReopenReplaysTheDurableFrame()
    {
        var shouldFail = false;
        var directory = Path.Combine(Path.GetTempPath(), "keyload-point-cache-poison-" + Guid.NewGuid().ToString("N"));
        using var budget = new CacheMemoryBudget(UnitAdmissionOptions.Cache(new()));
        try
        {
            {
                using var store = new ZoneTreeStore(new ZoneTreeStoreOptions(directory)
                {
                    EmbeddedPointCache = new ZoneTreePointCacheOptions(budget),
                    FaultObserver = (stage, _, _) =>
                    {
                        if (shouldFail && stage == CommitStage.MutationApplied)
                        {
                            throw new InvalidOperationException("Intentional apply observer failure.");
                        }
                    }
                }, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
                Put(store, ValueA);
                Warm(store);
                var before = store.GetPointCacheDiagnostics();
                shouldFail = true;
                var writeFailure = Assert.ThrowsExactly<KeyLoadException>(() => Put(store, [44]));
                var readFailure = Assert.ThrowsExactly<KeyLoadException>(() => store.Read(view => view.ReadOwnedValue(KeyA)));
                var after = store.GetPointCacheDiagnostics();
                await Assert.That(writeFailure.Code).IsEqualTo(ErrorCode.UnknownWriteOutcome);
                await Assert.That(readFailure.Code).IsEqualTo(ErrorCode.RecoveryRequired);
                await Assert.That(after.Hits).IsEqualTo(before.Hits);
            }

            await AssertRecoveredColdStoreAsync(directory, budget);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task AssertRecoveredColdStoreAsync(string directory, CacheMemoryBudget budget)
    {
        using var reopened = new ZoneTreeStore(new ZoneTreeStoreOptions(directory)
        {
            EmbeddedPointCache = new ZoneTreePointCacheOptions(budget)
        }, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var cold = reopened.GetPointCacheDiagnostics();
        AssertValue(reopened, [44]);
        var recovered = reopened.GetPointCacheDiagnostics();
        await Assert.That(cold.Hits).IsEqualTo(0L);
        await Assert.That(recovered.NativeLookups).IsEqualTo(1L);
        await Assert.That(recovered.Admissions).IsEqualTo(1L);
    }

    private static void Put(ZoneTreeStore store, byte[] value)
        => store.Commit((tx, _) => { tx.Put(KeyA, value); return true; });

    private static void Warm(ZoneTreeStore store)
        => _ = store.Read(view => view.ReadOwnedValue(KeyA));

    private static void AssertValue(ZoneTreeStore store, byte[] expected)
    {
        var actual = store.Read(view => view.ReadOwnedValue(KeyA));
        if (actual is null || !actual.AsSpan().SequenceEqual(expected))
        {
            throw new InvalidOperationException("Recovered native bytes did not match the durable write.");
        }
    }
}
