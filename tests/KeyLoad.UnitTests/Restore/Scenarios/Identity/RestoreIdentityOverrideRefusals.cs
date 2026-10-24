using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.BackupRestore;

internal static class RestoreIdentityOverrideRefusals
{
    internal static async Task RunAsync(RestoreIdentityOverrideFixture fixture, RestoreIdentityOverrideState state,
        bool emptyDestination, Guid freshIncarnation, byte[] freshKey, CancellationToken token)
    {
        var identities = new[]
        {
            (state.SourceIdentity.Incarnation, freshKey),
            (freshIncarnation, state.SourceIdentity.SigningKey.ToArray()),
            (state.SourceIdentity.Incarnation, state.SourceIdentity.SigningKey.ToArray())
        };
        foreach (var identity in identities)
        {
            token.ThrowIfCancellationRequested();
            KeyLoadException? failure = null;
            try
            {
                ZoneTreeStore.Restore(fixture.BackupDirectory, fixture.TargetDirectory, UnitExecutionOptions.StorageExecution(), identity.Item1, identity.Item2, token);
            }
            catch (KeyLoadException original)
            {
                failure = original;
            }
            await Assert.That(failure).IsNotNull();
            await Assert.That(failure!.Code).IsEqualTo(ErrorCode.Validation);
            await Assert.That(failure.Message).IsEqualTo(RestoreIdentityOverrideState.IdentityDetail);
            await Assert.That(Directory.Exists(fixture.TargetDirectory)).IsEqualTo(emptyDestination);
            if (emptyDestination)
            {
                await Assert.That(Directory.EnumerateFileSystemEntries(fixture.TargetDirectory).Any()).IsFalse();
            }
            var parent = Path.GetDirectoryName(fixture.TargetDirectory)!;
            await Assert.That(Directory.EnumerateDirectories(parent, ".keyload-restore-*").Any()).IsFalse();
            await RestoreIdentityOverrideAssertions.SourceAsync(fixture, state);
        }
    }
}
