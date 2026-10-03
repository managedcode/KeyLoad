using System.IO.Compression;
using System.Security.Cryptography;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteIsolatedGitHubEntryOperations
{
    public static async Task<SiteIsolatedGitHubArchiveFile> HashEntryAsync(ZipArchiveEntry entry,
        long maximum, CancellationToken token)
    {
        await using var stream = await entry.OpenAsync(token);
        return await TransferAsync(stream, null, entry.FullName, entry.Length, maximum, token);
    }

    public static async Task<SiteIsolatedGitHubArchiveFile> ExtractAsync(SiteIsolatedGitHubArchiveEntry entry,
        string stage, string prefix, CancellationToken token)
    {
        var relative = prefix + SiteIsolatedGitHubTokens.Slash + entry.File.Path;
        var target = SiteIsolatedGitHubFileOperations.FixedTarget(stage, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await using var source = await entry.Entry.OpenAsync(token);
        await using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var actual = await TransferAsync(source, destination, relative, entry.File.Bytes, entry.File.Bytes, token);
        if (actual.Sha256 != entry.File.Sha256)
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.Changed);
        }

        return actual;
    }

    private static async Task<SiteIsolatedGitHubArchiveFile> TransferAsync(Stream source, Stream? destination,
        string relative, long declared, long maximum, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[SiteIsolatedGitHubTokens.BufferBytes];
        long count = SiteIsolatedGitHubTokens.Zero;
        while (true)
        {
            var read = await source.ReadAsync(buffer, token);
            if (read == SiteIsolatedGitHubTokens.Zero)
            {
                break;
            }

            count = checked(count + read);
            if (count > declared || count > maximum)
            {
                throw new InvalidDataException(SiteIsolatedGitHubTokens.Bounds);
            }

            hash.AppendData(buffer.AsSpan(SiteIsolatedGitHubTokens.Zero, read));
            if (destination is not null)
            {
                await destination.WriteAsync(buffer.AsMemory(SiteIsolatedGitHubTokens.Zero, read), token);
            }
        }

        if (count != declared)
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.Changed);
        }

        return new(relative, count, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }
}
