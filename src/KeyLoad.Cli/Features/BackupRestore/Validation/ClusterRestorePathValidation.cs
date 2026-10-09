namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Admits only clean offline-owned destinations; it never deletes preexisting node directories.</summary>
internal static class ClusterRestorePathValidation
{
    private const string Invalid = "The restore paths must be clean, nonoverlapping operator-owned directories without links.";
    private const string ParentSegment = "..";

    internal static string Root(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        if (root == Path.GetPathRoot(root) || File.Exists(root)
            || Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        { throw Errors.Fail(ErrorCode.Conflict, Invalid); }
        RequireAncestors(root);
        return root;
    }

    internal static string Relative(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)
            || relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]).Contains(ParentSegment))
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(root, relative)));
        var prefix = root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.Ordinal) || full == root)
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
        RequireAncestors(full);
        return full;
    }

    internal static void RequireDistinct(IReadOnlyCollection<string> nodes)
    {
        if (nodes.Distinct(StringComparer.Ordinal).Count() != nodes.Count)
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
        foreach (var node in nodes)
        {
            if (nodes.Any(other => node != other
                && node.StartsWith(other + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            { throw Errors.Fail(ErrorCode.Validation, Invalid); }
        }
    }

    internal static void RequireAncestors(string path)
    {
        var current = new DirectoryInfo(path);
        while (current is not null)
        {
            if (File.Exists(current.FullName) || current.Exists
                && (current.Attributes & FileAttributes.ReparsePoint) != default)
            { throw Errors.Fail(ErrorCode.Validation, Invalid); }
            current = current.Parent;
        }
    }
}
