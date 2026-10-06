using Microsoft.Extensions.Options;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeBackupVerification
{
    private const string VerificationPrefix = "keyload-backup-verification-";
    private const string GuidFormat = "N";

    internal static (StoreIdentity Identity, long Position) Verify(string directory,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions)
    {
        var policy = executionOptions.Value;
        policy.Validate();
        var staging = Path.Combine(Path.GetTempPath(), VerificationPrefix + Guid.NewGuid().ToString(GuidFormat));
        ZoneTreeStoreFiles.CreatePrivateDirectory(staging);
        (StoreIdentity Identity, long Position) result;
        try
        {
            var identity = ZoneTreeBackupRestoreFiles.ReadAndVerify(directory, staging, policy, out var position);
            result = (identity, position);
        }
        catch (Exception failure)
        {
            RemoveStaging(staging, failure);
            throw;
        }

        RemoveStaging(staging);
        return result;
    }

    private static void RemoveStaging(string staging, Exception? verificationFailure = null)
    {
        try
        {
            Directory.Delete(staging, recursive: true);
        }
        catch (Exception cleanupFailure) when (verificationFailure is not null)
        {
            throw new AggregateException(verificationFailure, cleanupFailure);
        }
    }
}
