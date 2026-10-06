using System.Security.Cryptography;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal sealed class NativeCoverageImageSourceSnapshot
{
    private readonly IReadOnlyDictionary<string, string> files;

    private NativeCoverageImageSourceSnapshot(IReadOnlyDictionary<string, string> files) => this.files = files;

    internal static NativeCoverageImageSourceSnapshot Capture(string root, int maximumFiles, int maximumFileBytes, long maximumTotalBytes)
    {
        var identities = new Dictionary<string, string>(StringComparer.Ordinal);
        var fileCount = 0;
        long totalBytes = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (++fileCount > maximumFiles || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("The observed Release Server closure exceeds its admitted file inventory.");
            }
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 0 || info.Length > maximumFileBytes)
            {
                throw new InvalidDataException("A Release Server source file exceeds its configured file bound.");
            }
            totalBytes = checked(totalBytes + info.Length);
            if (totalBytes > maximumTotalBytes)
            { throw new InvalidDataException("The observed Release Server closure exceeds its configured total-byte bound."); }
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length != info.Length || stream.Length > maximumFileBytes)
            {
                throw new InvalidDataException("A Release Server source file changed beyond its configured bound.");
            }
            var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            identities.Add(Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'), hash);
        }
        return new(identities);
    }

    internal void VerifyUnchanged(string root, int maximumFiles, int maximumFileBytes, long maximumTotalBytes)
    {
        var current = Capture(root, maximumFiles, maximumFileBytes, maximumTotalBytes).files;
        if (files.Count != current.Count || files.Any(entry =>
            !current.TryGetValue(entry.Key, out var hash) || hash != entry.Value))
        {
            throw new InvalidDataException("A source Release Server file changed during image materialization.");
        }
    }
}
