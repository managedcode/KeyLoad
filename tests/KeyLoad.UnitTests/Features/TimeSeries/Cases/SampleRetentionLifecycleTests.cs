using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.TimeSeries;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleRetentionLifecycleTests
{
    private const string CustomerId = "customer-1";

    [Test]
    public async Task AcSeries016UnknownAndCorruptRetentionRecordsFailClosed()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        var key = SampleRetentionStateReader.Key(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series);
        db.Store.Commit((tx, _) =>
        {
            tx.PutRecord(key, new SampleRetentionState(SampleRetentionStateReader.CurrentFormatVersion + 1,
                SampleAggregateTestData.Start.UtcTicks, 0, false));
            return 0;
        });

        var unknown = Assert.ThrowsExactly<KeyLoadException>(() => db.Database.ReadSampleRetention(
            SampleAggregateTestData.RootPrincipal, new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series)));
        db.Store.Commit((tx, _) => { tx.Put(key, [1, 2, 3]); return 0; });
        var corrupt = Assert.ThrowsExactly<KeyLoadException>(() => db.Database.ReadSampleRetention(
            SampleAggregateTestData.RootPrincipal, new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series)));

        await Assert.That(unknown.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(corrupt.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcSeries016ReadCancellationLeavesFollowingReadHealthy()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.That(() => db.Database.ReadSampleRetention(SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series), cancelled.Token))
            .Throws<OperationCanceledException>();
        var healthy = db.Database.ReadSampleRetention(SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series));

        await Assert.That(healthy.Before).IsNull();
        await Assert.That(healthy.PurgedCount).IsEqualTo(0L);
    }

    [Test]
    public async Task AcSeries016RawDeleteScanBudgetRejectsBeforeChangingFloorOrSamples()
    {
        using var db = new TestDatabase(new DatabaseLimits { MaxQueryReadBytes = 128 });
        SampleAggregateTestData.Configure(db);
        var timestamp = SampleAggregateTestData.Start;
        var largeTags = "{\"payload\":\"" + new string('x', 512) + "\"}";
        SampleAggregateTestData.Append(db, [SampleAggregateTestData.Data(1, timestamp, 1)], largeTags);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => db.Commit(new ExpireSamples(
            SampleAggregateTestData.Set, SampleAggregateTestData.Series, timestamp.AddTicks(1), 1)));
        var key = SampleRetentionStateReader.Key(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series);
        var statusBytes = db.Store.Read(view => view.ReadOwnedValue(key));
        var samples = db.Store.Read(view => view.VisitRange(
            SampleReadKeys.Prefix(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series),
            2, static (_, _) => true));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(statusBytes).IsNull();
        await Assert.That(samples.Records).IsEqualTo(1);
    }

    [Test]
    public async Task AcSeries016ExpiryRejectsCorruptRecordsAndMismatchedSampleKeysWithoutDeleting()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        var timestamp = SampleAggregateTestData.Start;
        var key = KeySpace.Partition("sample", db.Partition, SampleAggregateTestData.Set,
            SampleAggregateTestData.Series, timestamp, 1L);
        SampleAggregateTestData.Append(db, [SampleAggregateTestData.Data(1, timestamp, 1)]);

        db.Store.Commit((tx, _) => { tx.Put(key, [1, 2, 3]); return 0; });
        var corrupt = Assert.ThrowsExactly<KeyLoadException>(() => db.Commit(new ExpireSamples(
            SampleAggregateTestData.Set, SampleAggregateTestData.Series, timestamp.AddTicks(1), 1)));
        var corruptValue = db.Store.Read(view => view.ReadOwnedValue(key));

        db.Store.Commit((tx, _) =>
        {
            tx.PutRecord(key, new SampleRecord("other-series", SampleAggregateTestData.Data(1, timestamp, 1), 1, "{}"));
            return 0;
        });
        var mismatched = Assert.ThrowsExactly<KeyLoadException>(() => db.Commit(new ExpireSamples(
            SampleAggregateTestData.Set, SampleAggregateTestData.Series, timestamp.AddTicks(1), 1)));
        var mismatchedValue = db.Store.Read(view => view.ReadOwnedValue(key));
        var retentionKey = SampleRetentionStateReader.Key(db.Partition,
            SampleAggregateTestData.Set, SampleAggregateTestData.Series);
        var retention = db.Store.Read(view => view.ReadOwnedValue(retentionKey));

        await Assert.That(corrupt.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(corruptValue).IsNotNull();
        await Assert.That(mismatched.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(mismatchedValue).IsNotNull();
        await Assert.That(retention).IsNull();
    }

    [Test]
    public async Task AcSeries016ExpiryWaitsForTheRealStoreReadGate()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        var cutoff = SampleAggregateTestData.Start.AddHours(1);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var commitStarted = new ManualResetEventSlim();
        var heldRead = Task.Factory.StartNew(() => db.Store.Read(view =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("The held read was not released.");
            }

            return 0;
        }), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        if (!entered.Wait(TimeSpan.FromSeconds(5)))
        {
            release.Set();
            await heldRead;
            throw new TimeoutException("The real store read gate was not acquired.");
        }

        var expiry = Task.Factory.StartNew(() =>
        {
            commitStarted.Set();
            return db.Commit(new ExpireSamples(SampleAggregateTestData.Set,
                SampleAggregateTestData.Series, cutoff));
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            if (!commitStarted.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("The expiry command did not start.");
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50));
            await Assert.That(expiry.IsCompleted).IsFalse();
        }
        finally
        {
            release.Set();
        }

        await Task.WhenAll(heldRead, expiry);
        var healthy = db.Database.ReadSampleRetention(SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series));
        await Assert.That(healthy.Before).IsEqualTo(cutoff);
    }

    [Test]
    public async Task AcSeries016ReopenPreservesFloorCountsAndPurgedSampleIdentitySequence()
    {
        var path = Path.Combine(Path.GetTempPath(), "keyload-series-retention-" + Guid.NewGuid().ToString("N"));
        ZoneTreeStore? store = null;
        try
        {
            store = new(new ZoneTreeStoreOptions(path));
            var first = Bootstrap(store);
            var start = SampleAggregateTestData.Start;
            var floor = start.AddSeconds(1);
            Commit(first, [new AppendSamples(SampleAggregateTestData.Set, SampleAggregateTestData.Series,
                [SampleAggregateTestData.Data(1, start, 1), SampleAggregateTestData.Data(2, floor, 2)])]);
            Commit(first, [new ExpireSamples(SampleAggregateTestData.Set, SampleAggregateTestData.Series, floor, 1)]);
            store.Dispose();
            store = null;

            store = new(new ZoneTreeStoreOptions(path));
            var reopened = new DatabaseEngine(store, new AuthorizationPolicy());
            var status = reopened.ReadSampleRetention(SampleAggregateTestData.RootPrincipal,
                new(new("tenant", "database", "orders", CustomerId),
                    SampleAggregateTestData.Set, SampleAggregateTestData.Series));
            Commit(reopened, [new AppendSamples(SampleAggregateTestData.Set, SampleAggregateTestData.Series,
                [SampleAggregateTestData.Data(1, start, 1), SampleAggregateTestData.Data(3, floor.AddTicks(1), 3)])]);
            var rows = reopened.ReadSamples(SampleAggregateTestData.RootPrincipal,
                new("tenant", "database", "orders", CustomerId), SampleAggregateTestData.Set,
                SampleAggregateTestData.Series, floor, floor.AddTicks(1));

            await Assert.That(status.Before).IsEqualTo(floor);
            await Assert.That(status.PurgedCount).IsEqualTo(1L);
            await Assert.That(status.HasMore).IsFalse();
            await Assert.That(rows.Select(row => row.Sequence)).IsEquivalentTo(new long[] { 2, 3 });
        }
        finally
        {
            store?.Dispose();
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }

    private static DatabaseEngine Bootstrap(ZoneTreeStore store)
    {
        var database = new DatabaseEngine(store, new AuthorizationPolicy());
        database.Bootstrap(new("root", "system", [new("*", "*", Capability.All)], ["*"]) { ClusterAdministrator = true },
            DatabaseEngine.Credential("root", "root", "root.unit-test-credential-32-characters"));
        database.Apply(new(Guid.NewGuid(), OperationKind.ConfigureResource, "root", TimeProvider.System.GetUtcNow(),
            JsonSerializer.Serialize(new ConfigureResourceRequest("tenant", "database",
                new ResourceDefinition(SampleAggregateTestData.Set, ResourceKind.TimeSeries, "orders")), JsonDefaults.Options)));
        return database;
    }

    private static CommitReceipt Commit(DatabaseEngine database, Mutation[] mutations)
    {
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, new("tenant", "database", "orders", CustomerId), [.. mutations]);
        return database.Apply(new(id, OperationKind.Batch, "root", TimeProvider.System.GetUtcNow(),
            JsonSerializer.Serialize(command, JsonDefaults.Options))).Get<CommitReceipt>();
    }
}
