using Microsoft.Extensions.Options;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeBackupVerification
{
    private const string VerificationPrefix = "keyload-backup-verification-";

    internal static (StoreIdentity Identity, long Position) Verify(string directory,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions)
    {
        var policy = executionOptions.Value;
        policy.Validate();
        var staging = Path.Combine(Path.GetTempPath(), VerificationPrefix + Guid.NewGuid().ToString("N"));
        ZoneTreeStoreFiles.CreatePrivateDirectory(staging);
        try
        {
            var identity = ZoneTreeBackupRestoreFiles.ReadAndVerify(directory, staging, policy, out var position);
            return (identity, position);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }
}
