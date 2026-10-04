using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteIsolatedGitHubFileOperations
{
    public static void RequireRegular(string path)
    {
        RequireSafeAncestors(path);
        var info = new FileInfo(path);
        if (!info.Exists || info.LinkTarget is not null || (info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.InvalidPath);
        }
    }

    public static void RequireSafeAncestors(string path)
    {
        if (!Path.IsPathFullyQualified(path) || Path.GetFullPath(path) != path)
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.InvalidPath);
        }

        var parent = Directory.GetParent(path);
        while (parent is not null)
        {
            if (!parent.Exists || parent.LinkTarget is not null || (parent.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(SiteIsolatedGitHubTokens.InvalidPath);
            }

            parent = parent.Parent;
        }
    }

    public static async Task<SiteIsolatedGitHubArchiveFile> HashAsync(string path, string relative,
        long maximum, CancellationToken token)
    {
        RequireRegular(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length <= SiteIsolatedGitHubTokens.Zero || stream.Length > maximum)
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.Bounds);
        }

        return await HashStreamAsync(stream, relative, maximum, token);
    }

    private static async Task<SiteIsolatedGitHubArchiveFile> HashStreamAsync(FileStream stream,
        string relative, long maximum, CancellationToken token)
    {
        var declared = stream.Length;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[SiteIsolatedGitHubTokens.BufferBytes];
        long count = SiteIsolatedGitHubTokens.Zero;
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) > SiteIsolatedGitHubTokens.Zero)
        {
            count = checked(count + read);
            if (count > maximum || count > declared)
            {
                throw new InvalidDataException(SiteIsolatedGitHubTokens.Bounds);
            }

            hash.AppendData(buffer.AsSpan(SiteIsolatedGitHubTokens.Zero, read));
        }

        if (count != declared || stream.Length != declared)
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.Changed);
        }

        return new(relative, count, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    public static void RequireArtifactIdentity(SiteIsolatedGitHubArchiveFile actual, JsonNode artifact)
    {
        if (actual.Bytes != artifact[SiteIsolatedGitHubTokens.SizeInBytes]!.GetValue<long>() ||
            SiteIsolatedGitHubTokens.DigestPrefix + actual.Sha256 != artifact[SiteIsolatedGitHubTokens.Digest]!.GetValue<string>())
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.Changed);
        }
    }

    public static string FixedTarget(string root, string relative)
    {
        var target = Path.GetFullPath(Path.Combine(root, relative.Replace(
            SiteIsolatedGitHubTokens.Slash, Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)));
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.InvalidPath);
        }

        return target;
    }

    public static void RequireAbsent(string path)
    {
        if (File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null)
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.Exists);
        }
    }
}
