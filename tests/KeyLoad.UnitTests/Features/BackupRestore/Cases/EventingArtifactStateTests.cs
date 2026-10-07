using KeyLoad.Artifacts;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class EventingArtifactStateTests
{
    private const int PieceBytes = 1_024;
    private const string Backup = "backup";
    private const string Artifact = "eventing.ctg";
    private const string Unpacked = "unpacked";
    private const string Target = "restored";

    [Test]
    public async Task AcBackup004LocalEventingArtifactRetainsGroupInboxQueueCutAndRequiresExplicitResume()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await EventingArtifactFixture.RunAsync(async fixture =>
        {
            await EventingArtifactSeed.RunAsync(fixture);
            var source = fixture.Source.Store;
            var domains = EventingArtifactState.Bytes(source, fixture.Source.Partition);
            var identity = source.Identity;
            var outcome = BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(source, fixture.Source.Partition,
                EventingArtifactFixture.Principal, fixture.Original.Id);
            var cut = source.CreateBackup(Path.Combine(fixture.Root, Backup));
            var manifest = await MetadataTestFiles.ReadManifestAsync(Path.Combine(fixture.Root, Backup));
            await Assert.That(manifest.Position).IsEqualTo(cut);
            await Assert.That(source.Position).IsEqualTo(cut);
            var archive = Path.Combine(fixture.Root, Artifact);
            BackupArtifact.Pack(Path.Combine(fixture.Root, Backup), archive, pieceBytes: PieceBytes);
            var originalBytes = await File.ReadAllBytesAsync(archive, token);
            BackupArtifact.Unpack(archive, Path.Combine(fixture.Root, Unpacked));
            var restored = ZoneTreeStore.Restore(Path.Combine(fixture.Root, Unpacked),
                Path.Combine(fixture.Root, Target), UnitExecutionOptions.StorageExecution());
            await Assert.That(restored.Incarnation).IsNotEqualTo(identity.Incarnation);
            await Assert.That(restored.DispatchPaused).IsTrue();
            fixture.OpenTarget(Path.Combine(fixture.Root, Target));
            await Assert.That(fixture.Target!.Position).IsEqualTo(cut + 1);
            await EventingArtifactState.AssertSeedAsync(fixture, fixture.Database);
            await EventingArtifactState.SameAsync(domains, fixture.Target, fixture.Source.Partition);
            await EventingArtifactFences.VerifyAsync(fixture);
            await Assert.That(BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(fixture.Target, fixture.Source.Partition,
                EventingArtifactFixture.Principal, fixture.Original.Id)).IsEquivalentTo(outcome, CollectionOrdering.Matching);
            await EventingArtifactContinuation.RunAsync(fixture);
            var finalPosition = fixture.Target.Position;
            var finalBytes = EventingArtifactState.Bytes(fixture.Target, fixture.Source.Partition);
            var finalStoreBytes = EventingArtifactState.FullBytes(fixture.Target);
            var finalIdentity = fixture.Target.Identity;
            fixture.ReopenTarget(Path.Combine(fixture.Root, Target));
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
            await Assert.That(await File.ReadAllBytesAsync(archive, token)).IsEquivalentTo(originalBytes, CollectionOrdering.Matching);
        });
    }
}
