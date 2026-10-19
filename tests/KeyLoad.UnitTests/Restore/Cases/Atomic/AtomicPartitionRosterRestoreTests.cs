using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class AtomicPartitionRosterRestoreTests
{
    private const long NoApplied = AtomicPartitionRosterRestoreFixture.LocalApplied;
    private const long FirstNewApplied = AtomicPartitionRosterRestoreFixture.NewApplied;

    [Test, Arguments(NoApplied), Arguments(FirstNewApplied)]
    public Task AcBackupRoster001ReplicatedHistoricalRowsSurviveFirstWriteColdAndSecondRestore(long newApplied)
        => AtomicPartitionRosterRestoreFixture.RunAsync(async fixture =>
        {
            await AtomicPartitionRosterRestoreAssertions.LiteralDocumentAsync(fixture, AtomicPartitionRosterRestoreFixture.SeedId);
            var before = fixture.Target.Position;
            var oldDigest = AtomicPartitionRosterRestoreAssertions.CanonicalDigest(fixture.Target);
            var old = fixture.Database.Apply(fixture.Original);
            await Assert.That(Errors.Code(old.Problem)).IsEqualTo(ErrorCode.TokenInvalidated);
            await Assert.That(fixture.Target.Position).IsEqualTo(before);
            await Assert.That(AtomicPartitionRosterRestoreAssertions.CanonicalDigest(fixture.Target)).IsEqualTo(oldDigest);
            await Assert.That(fixture.ReadOriginalOutcome().AsSpan().SequenceEqual(fixture.OriginalOutcome)).IsTrue();
            var operation = AtomicPartitionRosterRestoreFixture.Operation(fixture.Database, AtomicPartitionRosterRestoreFixture.NewId);
            var result = newApplied == NoApplied
                ? fixture.Database.Apply(operation)
                : fixture.Database.ApplyStorageOperation(operation, checked(fixture.Database.LastApplied + 1),
                    TestContext.Current!.Execution.CancellationToken).Result;
            await Assert.That(Errors.Code(result.Problem)).IsNull();
            var position = fixture.Target.Position;
            await ReplayResultAssertions.Same<CommitReceipt>(fixture.Database.Apply(operation), result);
            await Assert.That(fixture.Target.Position).IsEqualTo(position);
            fixture.Reopen();
            await ReplayResultAssertions.Same<CommitReceipt>(fixture.Database.Apply(operation), result);
            await Assert.That(fixture.Target.Position).IsEqualTo(position);
            await AtomicPartitionRosterRestoreAssertions.LiteralDocumentAsync(fixture, AtomicPartitionRosterRestoreFixture.NewId);
            fixture.RestoreAgain();
            await AtomicPartitionRosterRestoreAssertions.LiteralDocumentAsync(fixture, AtomicPartitionRosterRestoreFixture.SeedId);
            var second = AtomicPartitionRosterRestoreFixture.Operation(fixture.Database, AtomicPartitionRosterRestoreFixture.SecondId);
            await Assert.That(Errors.Code(fixture.Database.Apply(second).Problem)).IsNull();
            fixture.Reopen();
            await AtomicPartitionRosterRestoreAssertions.LiteralDocumentAsync(fixture, AtomicPartitionRosterRestoreFixture.SecondId);
            await fixture.AssertArchiveUnchangedAsync();
        });

}
