using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.BackupRestore;

internal sealed class EventingArtifactStateTests
{
    [Test]
    public async Task OldCursorAndLeasesStayFencedAfterAuthorizedResumeTwoColdAndHealthyDelivery()
        => await RestoreResumedTokenFlow.RunAsync(TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RestorePreCancellationPreservesArchiveThenSamePathRepairAndTwoColdOwners(bool existingEmpty)
        => await RestoreCancellationFlow.RunAsync(existingEmpty,
            TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);

    private const string Backup = "backup";
    private const string Target = "restored";

    [Test]
    public async Task AcBackup004LocalEventingBackupRetainsGroupInboxQueueCutAndRequiresExplicitResume()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await EventingArtifactFixture.RunAsync(async fixture =>
        {
            await EventingArtifactSeed.RunAsync(fixture);
            var source = fixture.Source.Store;
            var domains = EventingArtifactState.Bytes(source, fixture.Source.Partition);
            var identity = source.Identity;
            var outcome = BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(source, fixture.Source.Partition,
                EventingArtifactFixture.Principal, fixture.Original.CommandId);
            var backup = Path.Combine(fixture.Root, Backup);
            var cut = await source.CreateBackupAsync(backup, token);
            await Assert.That(source.Position).IsEqualTo(cut);
            var archive = await RestoreCancellationFlow.ArchiveAsync(backup, token);
            var restored = await ZoneTreeStore.RestoreAsync(backup, Path.Combine(fixture.Root, Target),
                UnitExecutionOptions.StorageExecution(), cancellationToken: token);
            await Assert.That(restored.Incarnation).IsNotEqualTo(identity.Incarnation);
            await Assert.That(restored.DispatchPaused).IsTrue();
            fixture.OpenTarget(Path.Combine(fixture.Root, Target));
            await Assert.That(fixture.Target!.Position).IsGreaterThan(0);
            await EventingArtifactState.AssertSeedAsync(fixture, fixture.Database);
            await EventingArtifactState.SameAsync(domains, fixture.Target, fixture.Source.Partition);
            await EventingArtifactFences.VerifyAsync(fixture);
            await Assert.That(BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(fixture.Target, fixture.Source.Partition,
                EventingArtifactFixture.Principal, fixture.Original.CommandId)).IsEquivalentTo(outcome, CollectionOrdering.Matching);
            await EventingArtifactContinuation.RunAsync(fixture);
            var finalPosition = fixture.Target.Position;
            var finalBytes = EventingArtifactState.Bytes(fixture.Target, fixture.Source.Partition);
            var finalStoreBytes = EventingArtifactState.FullBytes(fixture.Target);
            var finalIdentity = fixture.Target.Identity;
            fixture.ReopenTarget(Path.Combine(fixture.Root, Target));
            finalPosition = await NativeStoreAssertions.ReopenedAsync(fixture.Target!, finalPosition);
            await Assert.That(fixture.Target!.Position).IsEqualTo(finalPosition);
            await EventingArtifactState.SameAsync(finalBytes, fixture.Target, fixture.Source.Partition);
            await Assert.That(EventingArtifactState.FullBytes(fixture.Target)).IsEquivalentTo(finalStoreBytes, CollectionOrdering.Matching);
            await Assert.That(fixture.Target.Identity.Incarnation).IsEqualTo(finalIdentity.Incarnation);
            await EventingArtifactHealthyAssertions.CompletedAsync(fixture, fixture.Database);
            await Assert.That(fixture.Database.GetSubscription(EventingArtifactFixture.Principal, fixture.Group).Checkpoint).IsEqualTo(4L);
            await Assert.That(fixture.Database.ReadEventSource(EventingArtifactFixture.Principal, new(fixture.Events)).Head)
                .IsEqualTo(new EventSourceHead(4, 1, 1));
            await EventingArtifactState.AssertSeedAsync(fixture, fixture.Source.Database);
            await Assert.That(source.Position).IsEqualTo(cut);
            await BackupIndexedDocumentDedupAssertions.AssertArchiveUnchanged(backup, archive);
        });
    }
}
