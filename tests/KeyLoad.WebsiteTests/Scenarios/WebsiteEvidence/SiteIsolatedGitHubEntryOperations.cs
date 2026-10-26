using System.IO.Compression;
using System.Security.Cryptography;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal static class SiteIsolatedGitHubEntryOperations
{
    public static async Task<SiteIsolatedGitHubArchiveFile> HashEntryAsync(ZipArchiveEntry entry,
        long maximum, CancellationToken token)
    {
        await using var stream = await entry.OpenAsync(token);
        return await TransferAsync(stream, entry.FullName, entry.Length, maximum, token);
    }

    private static async Task<SiteIsolatedGitHubArchiveFile> TransferAsync(Stream source,
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

        }

        if (count != declared)
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.Changed);
        }

        return new(relative, count, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }
}
