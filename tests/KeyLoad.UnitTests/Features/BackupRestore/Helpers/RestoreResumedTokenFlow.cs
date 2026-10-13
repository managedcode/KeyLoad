using System.Text.Json;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class RestoreResumedTokenFlow
{
    private const string Backup = "resumed-token-backup";
    private const string Target = "resumed-token-target";
    private const int ColdOpens = 2;

    internal static async Task RunAsync(CancellationToken token)
    {
        await EventingArtifactFixture.RunAsync(async fixture =>
        {
            token.ThrowIfCancellationRequested();
            await EventingArtifactSeed.RunAsync(fixture);
            var sourceImage = EventingArtifactState.FullBytes(fixture.Source.Store);
            var backup = Path.Combine(fixture.Root, Backup);
            var target = Path.Combine(fixture.Root, Target);
            var cut = fixture.Source.Store.CreateBackup(backup);
            var files = Directory.GetFiles(backup).Order(StringComparer.Ordinal).ToArray();
            var archive = files.Select(path => Convert.ToHexString(File.ReadAllBytes(path))).ToArray();
            token.ThrowIfCancellationRequested();
            var identity = ZoneTreeStore.Restore(backup, target, UnitExecutionOptions.StorageExecution());
            fixture.OpenTarget(target);
            await Assert.That(identity.DispatchPaused).IsTrue();
            await Assert.That(identity.Incarnation).IsNotEqualTo(fixture.Source.Store.Identity.Incarnation);
            await EventingArtifactFences.VerifyAsync(fixture);
            await EventingArtifactContinuation.RunAsync(fixture);
            await Assert.That(fixture.Target!.Identity.DispatchPaused).IsFalse();
            var refused = await RestoreResumedTokenAssertions.RefuseAsync(fixture);
            await RestoreResumedTokenAssertions.ReplayAsync(fixture, refused);
            var image = EventingArtifactState.FullBytes(fixture.Target);
            var position = fixture.Target.Position;
            var currentIdentity = JsonSerializer.Serialize(fixture.Target.Identity, JsonDefaults.Options);
            for (var cold = 0; cold < ColdOpens; cold++)
            {
                fixture.ReopenTarget(target);
                await Assert.That(fixture.Target!.Position).IsEqualTo(position);
                await Assert.That(EventingArtifactState.FullBytes(fixture.Target))
                    .IsEquivalentTo(image, CollectionOrdering.Matching);
                await Assert.That(JsonSerializer.Serialize(fixture.Target.Identity, JsonDefaults.Options))
                    .IsEqualTo(currentIdentity);
                await Assert.That(fixture.Target.Identity.DispatchPaused).IsFalse();
                await RestoreResumedTokenAssertions.ReplayAsync(fixture, refused);
                await EventingArtifactHealthyAssertions.CompletedAsync(fixture, fixture.Database);
            }
            await RestoreResumedTokenHealthy.RunAsync(fixture);
            await Assert.That(fixture.Source.Store.Position).IsEqualTo(cut);
            await Assert.That(EventingArtifactState.FullBytes(fixture.Source.Store))
                .IsEquivalentTo(sourceImage, CollectionOrdering.Matching);
            await Assert.That(Directory.GetFiles(backup).Order(StringComparer.Ordinal).ToArray())
                .IsEquivalentTo(files, CollectionOrdering.Matching);
            await Assert.That(files.Select(path => Convert.ToHexString(File.ReadAllBytes(path))).ToArray())
                .IsEquivalentTo(archive, CollectionOrdering.Matching);
        });
    }
}
