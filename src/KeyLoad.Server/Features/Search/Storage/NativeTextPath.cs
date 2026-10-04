namespace KeyLoad.Server.Features.Search;

internal static class NativeTextPath
{
    internal static void VerifyExistingAncestors(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(current)
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw NativeTextErrors.Ownership();
            }
            current = Path.GetDirectoryName(current)!;
        }
    }

    internal static string Normalize(string root, string leaf, string path)
    {
        var native = Path.GetFullPath(Path.Combine(root, leaf, NativeTextProtocol.NativeDirectory));
        var full = Path.GetFullPath(path);
        if (!IsWithin(native, full))
        {
            throw NativeTextErrors.Ownership();
        }
        return Path.GetRelativePath(Path.Combine(root, leaf), full).Replace(Path.DirectorySeparatorChar, '/');
    }

    internal static bool IsWithin(string root, string path)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.GetFullPath(path);
        return string.Equals(normalizedRoot, normalizedPath, StringComparison.Ordinal)
            || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    internal static string RelativePath(string root, string path)
        => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    internal static void VerifyContainedAncestors(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root);
        var current = Path.GetFullPath(path);
        while (IsWithin(normalizedRoot, current))
        {
            if (Directory.Exists(current)
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw NativeTextErrors.Ownership();
            }
            if (string.Equals(current, normalizedRoot, StringComparison.Ordinal))
            {
                return;
            }
            current = Path.GetDirectoryName(current)!;
        }
        throw NativeTextErrors.Ownership();
    }
}
