using System.Security.Cryptography;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.BackupRestore.Helpers;

/// <summary>Captures a bounded digest of the verified fixture-owned backup manifest.</summary>
internal static class AdminBackupArchiveIntegrity
{
    private const string BackupManifestName = "backup.json";
    private const int EmptyManifestBytes = 0;
    private const string InvalidManifestLength = "The fixture backup manifest exceeds its native file bound.";
    private const string ChangedManifestLength = "The fixture backup manifest changed while its digest was captured.";

    internal static async Task<byte[]> CaptureManifestDigestAsync(string directory,
        ZoneTreeStorageExecutionOptions policy, CancellationToken cancellationToken)
    {
        using var manifest = new FileStream(Path.Combine(directory, BackupManifestName), new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            BufferSize = policy.StreamBufferBytes,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
        var length = manifest.Length;
        if (length <= EmptyManifestBytes || length > policy.MaximumBackupManifestBytes)
        { throw new InvalidDataException(InvalidManifestLength); }
        var digest = await SHA256.HashDataAsync(manifest, cancellationToken).ConfigureAwait(false);
        if (manifest.Position != length || manifest.Length != length)
        { throw new InvalidDataException(ChangedManifestLength); }
        return digest;
    }
}
