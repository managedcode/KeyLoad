using Microsoft.Extensions.Options;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeBackupVerification
{
    private const int FileStartPosition = 0;

    internal static (StoreIdentity Identity, long Position) Verify(string directory,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions, CancellationToken cancellationToken = default)
    {
        var policy = executionOptions.Value;
        policy.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = ZoneTreeBackupRestoreFiles.ReadManifestDefinition(directory, policy);
        var identityFile = manifest.Files.Single(file => file.Name == IdentityFileName);
        var identityBytes = ZoneTreeBackupRestoreFiles.ReadAndVerifyIdentityFile(directory, identityFile,
            policy.MaximumIdentityFileBytes, policy.StreamBufferBytes);
        var journalFile = manifest.Files.Single(file => file.Name == JournalFileName);
        using var journal = ZoneTreeBackupArchiveJournalOpen.Open(Path.Combine(directory, JournalFileName),
            policy.StreamBufferBytes);
        ZoneTreeBackupFileChecksum.Verify(journal, journalFile, new byte[policy.FileBufferBytes], cancellationToken);
        var identity = ZoneTreeIdentityFile.Read(identityBytes.Span);
        journal.Position = FileStartPosition;
        ZoneTreeBackupJournalValidation.Verify(journal, identity, manifest.Position, policy, cancellationToken);
        return (identity, manifest.Position);
    }
}
