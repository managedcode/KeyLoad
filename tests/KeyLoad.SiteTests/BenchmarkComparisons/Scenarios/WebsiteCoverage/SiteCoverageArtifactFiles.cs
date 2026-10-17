using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteCoverageArtifactFiles
{
    public static string[] EnumerateFiles(string directory, SiteCoverageCollection result)
    {
        if (!Directory.Exists(directory))
        {
            result.Errors.Add(SiteCoverageTokens.MissingNativeReceiptsFailure);
            return [];
        }

        if (!HasNoLinkedAncestors(RequiredCoverageRoot(), directory))
        {
            result.Errors.Add(SiteCoverageTokens.CoveragePathFailure);
            return [];
        }

        return EnumerateFilesNoLinks(directory, result).Order(StringComparer.Ordinal).ToArray();
    }

    public static async Task<byte[]> ReadBoundedAsync(string path, SiteCoverageCollection result,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < SiteCoverageTokens.Zero || info.Length > SiteCoverageTokens.MaximumNativeFileBytes ||
            ++result.FilesRead > SiteCoverageTokens.MaximumNativeFiles ||
            result.BytesRead + info.Length > SiteCoverageTokens.MaximumNativeBytes ||
            !HasNoLinkedAncestors(RequiredCoverageRoot(), path))
        {
            throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.InvalidCoverageFailure);
        }

        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            SiteTokens.ProcessOutputBufferCharacters, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var output = new MemoryStream((int)info.Length);
        var buffer = new byte[SiteTokens.ProcessOutputBufferCharacters];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == SiteCoverageTokens.Zero)
            {
                break;
            }

            if (output.Length + read > SiteCoverageTokens.MaximumNativeFileBytes ||
                result.BytesRead + output.Length + read > SiteCoverageTokens.MaximumNativeBytes)
            {
                throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.InvalidCoverageFailure);
            }

            await output.WriteAsync(buffer.AsMemory(SiteTokens.Zero, read), cancellationToken).ConfigureAwait(false);
        }

        result.BytesRead += output.Length;
        return output.ToArray();
    }

    public static IEnumerable<string> EnumerateFilesNoLinks(string directory, SiteCoverageCollection result)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                result.Errors.Add(SiteCoverageTokens.CoveragePathFailure);
                continue;
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                foreach (var child in EnumerateFilesNoLinks(entry, result))
                {
                    yield return child;
                }
            }
            else
            {
                yield return entry;
            }
        }
    }

    public static List<string> EnumerateDirectories(string directory, SiteCoverageCollection result)
    {
        if (!Directory.Exists(directory))
        {
            result.Errors.Add(SiteCoverageTokens.MissingNativeReceiptsFailure);
            return [];
        }

        if (!HasNoLinkedAncestors(RequiredCoverageRoot(), directory))
        {
            result.Errors.Add(SiteCoverageTokens.CoveragePathFailure);
            return [];
        }

        var directories = new List<string>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
            {
                result.Errors.Add(SiteCoverageTokens.CoveragePathFailure);
                continue;
            }

            directories.Add(entry);
        }

        return directories;
    }

    public static bool IsLoopbackOrigin(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == SiteCoverageTokens.HttpScheme &&
            uri.Host == SiteCoverageTokens.LoopbackAddress && uri.Port > SiteCoverageTokens.Zero &&
            uri.AbsolutePath == SiteCoverageTokens.Slash && uri.UserInfo.Length == SiteCoverageTokens.Zero;
    }

    public static bool IsSessionId(string value) => value.Length > SiteCoverageTokens.Zero &&
        value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    public static int ReadInt32(JsonElement value, string property)
    {
        var item = value.GetProperty(property);
        return item.TryGetInt32(out var number) ? number : throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.JsonFailure);
    }

    public static string RelativePath(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot, PathComparison))
        {
            throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.CoveragePathFailure);
        }

        return Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, SiteCoverageTokens.Slash[SiteCoverageTokens.Zero]);
    }

    public static string RequiredCoverageRoot()
    {
        var root = Environment.GetEnvironmentVariable(SiteCoverageTokens.CoverageRootEnvironment);
        return root is not null && Path.IsPathFullyQualified(root)
            ? Path.GetFullPath(root) : throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.InvalidRootFailure);
    }

    public static bool HasNoLinkedAncestors(string root, string path)
    {
        var current = Path.GetFullPath(root);
        var relative = Path.GetRelativePath(current, Path.GetFullPath(path));
        if (Path.IsPathRooted(relative) || relative.StartsWith(SiteCoverageTokens.ParentSegmentMarker,
                StringComparison.Ordinal) || IsLink(current))
        {
            return false;
        }

        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if (IsLink(current))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    public static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static bool IsCoverageReadFailure(Exception exception) => exception is IOException or JsonException or
        InvalidDataException or InvalidOperationException or FormatException or UriFormatException or ArgumentException or
        OverflowException or UnauthorizedAccessException;

    public static HashSet<string> Set(params string[] fields) => new(fields, StringComparer.Ordinal);

}
