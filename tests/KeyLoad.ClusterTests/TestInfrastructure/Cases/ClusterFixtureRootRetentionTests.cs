using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.ClusterTests.TestInfrastructure;

/// <summary>Actual local root-release policy and execution store ownership; no injected Aspire stop or Orleans proof.</summary>
internal sealed class ClusterFixtureRootRetentionTests
{
    private const string RootPrefix = "keyload-fixture-root-retention-";
    private const string GuidFormat = "N";
    private const string StoreDirectory = "database";
    private const string SentinelFile = "original-owner.txt";
    private const string SentinelValue = "Retain the original failed-cleanup owner.";
    private const string FailureMarker = "Original fixture cleanup failure marker.";
    private const string LiveDeleteRefused = "The original live store must never reach root deletion.";
    private const string ImageExceeded = "The original execution fixture image exceeded its bound.";
    private const int MaximumRecords = 32;
    private const int NoDeletes = 0;
    private const int OneDelete = 1;
    private const int OneFailure = 1;
    private const int FirstFailureIndex = 0;
    private const byte StoredKey = 0x21;
    private const byte StoredValue = 0x42;
    private const int NoFailures = 0;
    private static readonly byte[] Key = [StoredKey];
    private static readonly byte[] Value = [StoredValue];

    [Test]
    public async Task FailedCleanupRetainsOwnerThenExactReopenAndHealthyRelease()
    {
        var root = Path.Combine(AppContext.BaseDirectory, RootPrefix + Guid.NewGuid().ToString(GuidFormat));
        var directory = Path.Combine(root, StoreDirectory);
        var failures = new List<Exception>();
        var released = false;
        ZoneTreeStore? active = null;
        Snapshot expected = null!;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, SentinelFile), SentinelValue);
            using (var store = new ZoneTreeStore(new(directory), IntegrationExecutionOptions.StorageExecution(),
                IntegrationExecutionOptions.PointCacheExecution()))
            {
                active = store;
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    store.Commit((transaction, _) => { transaction.Put(Key, Value); return true; });
                    expected = Capture(store);
                    await RetainAsync(root, store, expected, () => active is not null);
                }, failures);
            }
            active = null;
            released = true;
            if (failures.Count != NoFailures)
            {
                return;
            }
            released = false;
            using (var reopened = new ZoneTreeStore(new(directory)
            {
                Incarnation = expected.Incarnation
            }, IntegrationExecutionOptions.StorageExecution(), IntegrationExecutionOptions.PointCacheExecution()))
            {
                await ServerFailureObserver.ObserveAsync(() => VerifyAsync(root, reopened, expected), failures);
            }
            released = true;
            if (failures.Count == NoFailures)
            {
                await ReleaseAsync(root);
            }
        }, failures);
        if (failures.Count == NoFailures && released && Directory.Exists(root))
        {
            ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    [Test]
    public async Task NeverBuiltWithoutCleanupFailuresReleasesItsOwnedRoot()
    {
        var root = Path.Combine(AppContext.BaseDirectory, RootPrefix + Guid.NewGuid().ToString(GuidFormat));
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, SentinelFile), SentinelValue);
            await ReleaseAsync(root);
        }, failures);
        if (failures.Count == NoFailures && Directory.Exists(root))
        {
            ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RetainAsync(string root, ZoneTreeStore store,
        Snapshot expected, Func<bool> hasLiveOwner)
    {
        var original = new IOException(FailureMarker);
        var originals = new List<Exception> { original };
        var deletes = NoDeletes;
        Task DeleteAsync()
        {
            deletes++;
            if (hasLiveOwner())
            {
                throw new InvalidOperationException(LiveDeleteRefused);
            }
            Directory.Delete(root, recursive: true);
            return Task.CompletedTask;
        }
        var incomplete = await PostgresClusterFixtureOwnership.ReleaseRootAsync(false, originals, DeleteAsync);
        await Assert.That(incomplete).IsFalse();
        await Assert.That(deletes).IsEqualTo(NoDeletes);
        await Assert.That(originals.Count).IsEqualTo(OneFailure);
        await Assert.That(ReferenceEquals(originals[FirstFailureIndex], original)).IsTrue();
        await VerifyAsync(root, store, expected);
    }

    private static async Task ReleaseAsync(string root)
    {
        var failures = new List<Exception>();
        var deletes = NoDeletes;
        Task DeleteAsync()
        {
            deletes++;
            Directory.Delete(root, recursive: true);
            return Task.CompletedTask;
        }
        var incomplete = await PostgresClusterFixtureOwnership.ReleaseRootAsync(false, failures, DeleteAsync);
        await Assert.That(incomplete).IsFalse();
        await Assert.That(failures).IsEmpty();
        await Assert.That(deletes).IsEqualTo(OneDelete);
        await Assert.That(Directory.Exists(root)).IsFalse();
    }

    private static async Task VerifyAsync(string root, ZoneTreeStore store, Snapshot expected)
    {
        var actual = Capture(store);
        await Assert.That(Directory.Exists(root)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(root, SentinelFile))).IsEqualTo(SentinelValue);
        await Assert.That(actual.NodeId).IsEqualTo(expected.NodeId);
        await Assert.That(actual.Incarnation).IsEqualTo(expected.Incarnation);
        await Assert.That(actual.Cut).IsEqualTo(expected.Cut);
        await Assert.That(actual.Identity.AsSpan().SequenceEqual(expected.Identity)).IsTrue();
        await Assert.That(actual.SigningKey.AsSpan().SequenceEqual(expected.SigningKey)).IsTrue();
        await Assert.That(actual.Records.AsSpan().SequenceEqual(expected.Records)).IsTrue();
        await Assert.That(store.Read(view => view.ReadOwnedValue(Key))!.AsSpan().SequenceEqual(Value)).IsTrue();
    }

    private static Snapshot Capture(ZoneTreeStore store) => store.Read(view =>
    {
        var page = view.Scan([], MaximumRecords);
        if (page.HasMore)
        {
            throw new InvalidOperationException(ImageExceeded);
        }
        return new Snapshot(store.Identity.NodeId, store.Identity.Incarnation, store.Position,
            JsonDefaults.Serialize(store.Identity), store.Identity.SigningKey.ToArray(), page.Records.Select(record => Convert.ToHexString(record.Key.Span)
                + ":" + Convert.ToHexString(record.Value.Span)).ToArray());
    });

    private sealed record Snapshot(Guid NodeId, Guid Incarnation, long Cut, byte[] Identity, byte[] SigningKey, string[] Records);
}
