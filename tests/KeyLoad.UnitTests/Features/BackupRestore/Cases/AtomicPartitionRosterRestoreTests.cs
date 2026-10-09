using KeyLoad.UnitTests.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class AtomicPartitionRosterRestoreTests
{
    private const long NoApplied = AtomicPartitionRosterRestoreFixture.LocalApplied;
    private const long FirstNewApplied = AtomicPartitionRosterRestoreFixture.NewApplied;
    private const long NoHistoricalBound = 0;
    private const int WrongVersion = 2;

    [Test, Arguments(NoApplied), Arguments(FirstNewApplied)]
    public Task AcBackupRoster001ReplicatedHistoricalRowsSurviveFirstWriteColdAndSecondRestore(long newApplied)
        => AtomicPartitionRosterRestoreFixture.RunAsync(async fixture =>
        {
            await AtomicPartitionRosterRestoreAssertions.LiteralDocumentAsync(fixture, AtomicPartitionRosterRestoreFixture.SeedId);
            var before = fixture.Target.Position;
            var oldDigest = AtomicPartitionRosterRestoreAssertions.CanonicalDigest(fixture.Target);
            var old = fixture.Database.Apply(fixture.Original);
            await Assert.That(old.Error).IsEqualTo(ErrorCode.TokenInvalidated);
            await Assert.That(fixture.Target.Position).IsEqualTo(before);
            await Assert.That(AtomicPartitionRosterRestoreAssertions.CanonicalDigest(fixture.Target)).IsEqualTo(oldDigest);
            await Assert.That(fixture.ReadOriginalOutcome().AsSpan().SequenceEqual(fixture.OriginalOutcome)).IsTrue();
            var operation = AtomicPartitionRosterRestoreFixture.Operation(fixture.Database, AtomicPartitionRosterRestoreFixture.NewId);
            var result = fixture.Database.Apply(operation, newApplied);
            await Assert.That(result.Error).IsNull();
            var position = fixture.Target.Position;
            await NativeReplayResultAssertions.Same<CommitReceipt>(fixture.Database.Apply(operation), result);
            await Assert.That(fixture.Target.Position).IsEqualTo(position);
            fixture.Reopen();
            await NativeReplayResultAssertions.Same<CommitReceipt>(fixture.Database.Apply(operation), result);
            await Assert.That(fixture.Target.Position).IsEqualTo(position);
            await AtomicPartitionRosterRestoreAssertions.LiteralDocumentAsync(fixture, AtomicPartitionRosterRestoreFixture.NewId);
            fixture.RestoreAgain();
            await AtomicPartitionRosterRestoreAssertions.LiteralDocumentAsync(fixture, AtomicPartitionRosterRestoreFixture.SeedId);
            var second = AtomicPartitionRosterRestoreFixture.Operation(fixture.Database, AtomicPartitionRosterRestoreFixture.SecondId);
            await Assert.That(fixture.Database.Apply(second).Error).IsNull();
            fixture.Reopen();
            await AtomicPartitionRosterRestoreAssertions.LiteralDocumentAsync(fixture, AtomicPartitionRosterRestoreFixture.SecondId);
            await fixture.AssertArchiveUnchangedAsync();
        });

    [Test]
    public Task AcBackupRoster002ForgedHistoricalOriginRefusesWithoutEffectsThenOriginalIsHealthy()
        => AtomicPartitionRosterRestoreFixture.RunAsync(async fixture =>
        {
            var key = AtomicPartitionRosterRestoreOriginSerialization.OriginKey(AtomicPartitionRosterFixture.Source);
            var original = fixture.Target.Read(view => view.GetRecord<AtomicPartitionRosterRestoreOrigin>(key))!;
            var variants = new[] { original with { Version = WrongVersion }, original with { AppliedUpperBound = NoHistoricalBound },
                original with { Partition = AtomicPartitionRosterFixture.Destination }, original with { SourceIncarnation = Guid.Empty },
                original with { SourceIncarnation = Guid.NewGuid() },
                original with { RestoredIncarnation = Guid.NewGuid() }, original with { EntryDigest = [] } };
            foreach (var variant in variants)
            {
                fixture.Target.Commit((transaction, _) => { transaction.PutRecord(key, variant); return true; });
                var position = fixture.Target.Position;
                var digest = AtomicPartitionRosterRestoreAssertions.CanonicalDigest(fixture.Target);
                var operation = AtomicPartitionRosterRestoreFixture.Operation(fixture.Database, AtomicPartitionRosterRestoreFixture.NewId);
                var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.Apply(operation));
                await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
                await Assert.That(fixture.Target.Position).IsEqualTo(position);
                await Assert.That(AtomicPartitionRosterRestoreAssertions.CanonicalDigest(fixture.Target)).IsEqualTo(digest);
                fixture.Target.Commit((transaction, _) => { transaction.PutRecord(key, original); return true; });
            }
            var healthy = AtomicPartitionRosterRestoreFixture.Operation(fixture.Database, AtomicPartitionRosterRestoreFixture.NewId);
            await Assert.That(fixture.Database.Apply(healthy).Error).IsNull();
            fixture.Reopen();
            await AtomicPartitionRosterRestoreAssertions.LiteralDocumentAsync(fixture, AtomicPartitionRosterRestoreFixture.NewId);
            await fixture.AssertArchiveUnchangedAsync();
        });

    [Test]
    public Task AcBackupRoster002MissingOriginAndChangedHistoricalRowRefuseThenExactRepairIsHealthy()
        => AtomicPartitionRosterRestoreFixture.RunAsync(async fixture =>
        {
            var partition = AtomicPartitionRosterFixture.Source;
            var key = AtomicPartitionRosterRestoreOriginSerialization.OriginKey(partition);
            var origin = fixture.Target.Read(view => view.ReadOwnedValue(key))!;
            fixture.Target.Commit((transaction, _) => { transaction.Delete(key); return true; });
            await RequireNoEffectCorruptionAsync(fixture);
            fixture.Target.Commit((transaction, _) => { transaction.Put(key, origin); return true; });
            var entryKey = AtomicPartitionRosterRestoreOriginSerialization.EntryKey(partition);
            var row = NativeSerialization.Deserialize<AtomicPartitionCatalogEntryV1>(fixture.OriginalRoster);
            fixture.Target.Commit((transaction, _) =>
            { transaction.PutRecord(entryKey, row with { FirstSeenAppliedIndex = FirstNewApplied }); return true; });
            await RequireNoEffectCorruptionAsync(fixture);
            fixture.Target.Commit((transaction, _) => { transaction.Put(entryKey, fixture.OriginalRoster); return true; });
            var healthy = AtomicPartitionRosterRestoreFixture.Operation(fixture.Database, AtomicPartitionRosterRestoreFixture.NewId);
            await Assert.That(fixture.Database.Apply(healthy).Error).IsNull();
            fixture.Reopen();
            await AtomicPartitionRosterRestoreAssertions.LiteralDocumentAsync(fixture, AtomicPartitionRosterRestoreFixture.NewId);
            await fixture.AssertArchiveUnchangedAsync();
        });

    [Test]
    public Task AcBackupRoster002CorruptPriorOriginCannotPublishRestoreAndExactRepairContinuesCold()
        => AtomicPartitionRosterRestoreFixture.RunAsync(async fixture =>
        {
            var key = AtomicPartitionRosterRestoreOriginSerialization.OriginKey(AtomicPartitionRosterFixture.Source);
            var origin = fixture.Target.Read(view => view.GetRecord<AtomicPartitionRosterRestoreOrigin>(key))!;
            fixture.Target.Commit((transaction, _) =>
            { transaction.PutRecord(key, origin with { Version = WrongVersion }); return true; });
            await fixture.RequireCorruptBackupRejectedAsync();
            fixture.Target.Commit((transaction, _) => { transaction.PutRecord(key, origin); return true; });
            fixture.RestoreAgain();
            var healthy = AtomicPartitionRosterRestoreFixture.Operation(fixture.Database, AtomicPartitionRosterRestoreFixture.SecondId);
            await Assert.That(fixture.Database.Apply(healthy).Error).IsNull();
            fixture.Reopen();
            await AtomicPartitionRosterRestoreAssertions.LiteralDocumentAsync(fixture, AtomicPartitionRosterRestoreFixture.SecondId);
            await fixture.AssertArchiveUnchangedAsync();
        });

    [Test]
    public Task AcBackupRoster002OwningRestoreIdentityCannotBeMissingOrForgedBeforeHealthyContinuation()
        => AtomicPartitionRosterRestoreFixture.RunAsync(async fixture =>
        {
            var key = AtomicPartitionRosterRestoreOriginSerialization.IdentityKey();
            var bytes = fixture.Target.Read(view => view.ReadOwnedValue(key))!;
            var identity = NativeSerialization.Deserialize<AtomicPartitionRosterRestoreIdentity>(bytes);
            var variants = new[] { identity with { Version = WrongVersion },
                identity with { SourceIncarnation = Guid.NewGuid() }, identity with { RestoredIncarnation = Guid.NewGuid() } };
            foreach (var variant in variants)
            {
                fixture.Target.Commit((transaction, _) => { transaction.PutRecord(key, variant); return true; });
                await RequireNoEffectCorruptionAsync(fixture);
                fixture.Target.Commit((transaction, _) => { transaction.Put(key, bytes); return true; });
            }
            fixture.Target.Commit((transaction, _) => { transaction.Delete(key); return true; });
            await RequireNoEffectCorruptionAsync(fixture);
            fixture.Target.Commit((transaction, _) => { transaction.Put(key, bytes); return true; });
            var healthy = AtomicPartitionRosterRestoreFixture.Operation(fixture.Database, AtomicPartitionRosterRestoreFixture.NewId);
            await Assert.That(fixture.Database.Apply(healthy).Error).IsNull();
            fixture.Reopen();
            await AtomicPartitionRosterRestoreAssertions.LiteralDocumentAsync(fixture, AtomicPartitionRosterRestoreFixture.NewId);
            await fixture.AssertArchiveUnchangedAsync();
        });

    private static async Task RequireNoEffectCorruptionAsync(AtomicPartitionRosterRestoreFixture fixture)
    {
        var position = fixture.Target.Position;
        var digest = AtomicPartitionRosterRestoreAssertions.CanonicalDigest(fixture.Target);
        var operation = AtomicPartitionRosterRestoreFixture.Operation(fixture.Database, AtomicPartitionRosterRestoreFixture.NewId);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.Apply(operation));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(fixture.Target.Position).IsEqualTo(position);
        await Assert.That(AtomicPartitionRosterRestoreAssertions.CanonicalDigest(fixture.Target)).IsEqualTo(digest);
    }
}
