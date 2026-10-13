using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class RestoreIdentityOverrideFlow
{
    internal static async Task RunAsync(bool emptyDestination, CancellationToken token)
    {
        var failures = new List<Exception>();
        var root = Path.Combine(Path.GetTempPath(), "keyload-restore-identity-" + Guid.NewGuid().ToString("N"));
        var ownsRoot = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            if (Directory.Exists(root))
            { throw new InvalidOperationException("The restore flow requires a fresh owned root."); }
            Directory.CreateDirectory(root);
            ownsRoot = true;
            var fixture = new RestoreIdentityOverrideFixture(root);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                token.ThrowIfCancellationRequested();
                var state = await SeedAsync(fixture);
                if (emptyDestination)
                { Directory.CreateDirectory(fixture.TargetDirectory); }
                var freshIncarnation = Guid.NewGuid();
                var freshKey = state.SourceIdentity.SigningKey.ToArray();
                freshKey[0] ^= 1;
                await RestoreIdentityOverrideRefusals.RunAsync(fixture, state, emptyDestination,
                    freshIncarnation, freshKey, token);
                var identity = ZoneTreeStore.Restore(fixture.BackupDirectory, fixture.TargetDirectory,
                    UnitExecutionOptions.StorageExecution(), freshIncarnation, freshKey);
                await Assert.That(identity.Incarnation).IsEqualTo(freshIncarnation);
                await Assert.That(identity.SigningKey.Span.SequenceEqual(freshKey)).IsTrue();
                await Assert.That(identity.NodeId).IsNotEqualTo(state.SourceIdentity.NodeId);
                await Assert.That(identity.DispatchPaused).IsTrue();
                var saved = await RestoreIdentityOverrideCold.WarmAsync(fixture, state, identity, token);
                for (var cold = 0; cold < 2; cold++)
                { await RestoreIdentityOverrideCold.ReopenAsync(fixture, state, identity, saved, token); }
                await RestoreIdentityOverrideAssertions.SourceAsync(fixture, state);
            }, failures);
            ServerFailureObserver.Observe(fixture.Source.JoinOwnedResources, failures);
        }, failures);
        if (ownsRoot)
        { ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<RestoreIdentityOverrideState> SeedAsync(RestoreIdentityOverrideFixture fixture)
    {
        fixture.Source.Configure(RestoreIdentityOverrideState.Collection, ResourceKind.Collection);
        var seed = fixture.Batch(fixture.Source.Database, RestoreIdentityOverrideState.SeedId,
            new PutDocument(RestoreIdentityOverrideState.Collection, RestoreIdentityOverrideState.FirstId,
                RestoreIdentityOverrideState.FirstJson, 0));
        var result = fixture.Source.Database.Apply(seed);
        await RestoreIdentityOverrideAssertions.ReceiptAsync(result, RestoreIdentityOverrideState.SeedId,
            RestoreIdentityOverrideState.FirstId);
        var cut = fixture.CreateBackup();
        var manifest = await MetadataTestFiles.ReadManifestAsync(fixture.BackupDirectory);
        await Assert.That(manifest.Position).IsEqualTo(cut);
        return new(seed, result, BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(fixture.Source.Store,
            fixture.Partition, RestoreIdentityOverrideState.Principal, RestoreIdentityOverrideState.SeedId),
            fixture.Source.Store.Identity, cut, QueueWholeFlowStorage.Bytes(fixture.Source.Store),
            await fixture.CaptureArchiveAsync());
    }
}
