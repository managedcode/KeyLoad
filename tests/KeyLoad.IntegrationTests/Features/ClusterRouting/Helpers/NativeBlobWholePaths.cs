using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeBlobWholePaths
{
    internal static string RequireNode(string root, string node)
    {
        if (!TwoRf3MembershipProtocol.Nodes.Contains(node, StringComparer.Ordinal)) { throw Invalid(); }
        var directory = ExistingDirectory(Path.Combine(root, node));
        var owner = Path.Combine(directory, PartitionStoreProtocol.OwnershipFile);
        RequireRegular(owner);
        if (new FileInfo(owner).Length != 0) { throw Invalid(); }
        return directory;
    }
    internal static string Canonical(string node)
        => ExistingDirectory(Path.Combine(node, PartitionStoreProtocol.CanonicalDirectory));
    internal static string Replica(string node)
        => ExistingDirectory(Path.Combine(node, ReplicaProtocol.ReplicaDirectory));
    internal static string Identity(string directory)
    {
        var path = Path.Combine(directory, ZoneTreePersistenceFormat.IdentityFileName);
        RequireRegular(path);
        return path;
    }
    private static string ExistingDirectory(string path)
    {
        if (!Path.IsPathFullyQualified(path) || Path.GetFullPath(path) != path) { throw Invalid(); }
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
        {
            if (!current.Exists || (current.Attributes & FileAttributes.ReparsePoint) != 0) { throw Invalid(); }
        }
        return path;
    }
    private static void RequireRegular(string path)
    {
        if (!File.Exists(path) || (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        { throw Invalid(); }
    }
    private static InvalidOperationException Invalid() => new("The stopped original Blob owner path is invalid.");
}
