namespace KeyLoad.UnitTests;

internal static class TestDatabaseReplicaDirectory
{
    internal const string Suffix = "-native-replica";
    private const string Invalid = "The native fixture replica directory is not its original owned sibling.";

    internal static string Require(string canonicalDirectory, string replicaDirectory, Guid? originalNodeId)
    {
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(canonicalDirectory));
        var replica = Path.TrimEndingDirectorySeparator(Path.GetFullPath(replicaDirectory));
        var root = Path.GetDirectoryName(canonical);
        if (root is null || !Directory.Exists(root)
            || !string.Equals(Path.GetDirectoryName(replica), root, StringComparison.Ordinal)
            || !string.Equals(Path.GetFileName(replica), Path.GetFileName(canonical) + Suffix, StringComparison.Ordinal)
            || (new DirectoryInfo(root).Attributes & FileAttributes.ReparsePoint) != 0
            || (Directory.Exists(canonical) && (new DirectoryInfo(canonical).Attributes & FileAttributes.ReparsePoint) != 0)
            || new DirectoryInfo(replica).LinkTarget is not null
            || File.Exists(replica)
            || originalNodeId == Guid.Empty
            || (Directory.Exists(replica) && (new DirectoryInfo(replica).Attributes & FileAttributes.ReparsePoint) != 0)
            || (originalNodeId is null && Directory.Exists(replica))
            || (originalNodeId is not null && !Directory.Exists(replica)))
        { throw new InvalidOperationException(Invalid); }
        return replica;
    }

    internal static void RequireOriginal(Guid actualNodeId, Guid? originalNodeId)
    {
        if (originalNodeId is { } expected && actualNodeId != expected)
        { throw new InvalidOperationException(Invalid); }
    }
}
