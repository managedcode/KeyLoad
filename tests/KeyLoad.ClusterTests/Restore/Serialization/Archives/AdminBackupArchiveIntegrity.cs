using System.Security.Cryptography;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.ClusterTests.BackupRestore.Helpers;

/// <summary>Captures one digest over every fixture-owned backup file and its relative path.</summary>
internal static class AdminBackupArchiveIntegrity
{
    private const string AllFiles = "*";
    private const string ChangedFileLength = "A fixture backup file changed while its digest was captured.";

    internal static async Task<byte[]> CaptureArchiveDigestAsync(string directory,
        ZoneTreeStorageExecutionOptions policy, CancellationToken cancellationToken)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in Directory.EnumerateFiles(directory, AllFiles, SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal))
        {
            digest.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(directory, path)));
            using var file = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                BufferSize = policy.StreamBufferBytes,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });
            var length = file.Length;
            digest.AppendData(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
            if (file.Position != length || file.Length != length)
            {
                throw new InvalidDataException(ChangedFileLength);
            }
        }
        return digest.GetHashAndReset();
    }
}
