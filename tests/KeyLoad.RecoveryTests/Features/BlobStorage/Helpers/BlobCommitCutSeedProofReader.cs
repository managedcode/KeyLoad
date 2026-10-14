using System.Text.Json;
using KeyLoad.CrashHost.Features.BlobStorage;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobCommitCutSeedProofReader
{
    internal static async Task<BlobCommitCutSeedProof> LoadAsync(string root, CancellationToken cancellationToken)
    {
        var bytes = await ReadAsync(root, BlobCommitCutProtocol.SeedFile, cancellationToken);
        return JsonSerializer.Deserialize<BlobCommitCutSeedProof?>(bytes, JsonDefaults.Options)
            ?? throw new InvalidOperationException("The original native blob seed proof is absent.");
    }

    internal static async Task<byte[]> ReadAsync(string root, string name, CancellationToken cancellationToken)
    {
        var file = Path.Combine(root, name);
        var info = new FileInfo(file);
        if (!info.Exists || info.Length <= 0 || info.Length > BlobCommitCutProtocol.MaximumSidecarBytes)
        {
            throw new InvalidOperationException("The bounded original blob sidecar is absent or invalid.");
        }
        var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
        if (bytes.Length > BlobCommitCutProtocol.MaximumSidecarBytes)
        {
            throw new InvalidOperationException("The original blob sidecar exceeded its bound.");
        }
        return bytes;
    }
}
