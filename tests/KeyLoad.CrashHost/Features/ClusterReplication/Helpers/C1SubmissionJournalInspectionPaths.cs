using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.ClusterReplication;

internal static class C1SubmissionJournalInspectionPaths
{
    private const int EmptyLength = 0;
    internal static string Canonical(C1SubmissionJournalInspectionRequest request)
        => ExistingDirectory(Path.Combine(request.NodeDirectory, PartitionStoreProtocol.CanonicalDirectory));
    internal static string Replica(C1SubmissionJournalInspectionRequest request)
        => ExistingDirectory(Path.Combine(request.NodeDirectory, ReplicaProtocol.ReplicaDirectory));
    internal static string Owner(C1SubmissionJournalInspectionRequest request)
        => Path.Combine(request.NodeDirectory, PartitionStoreProtocol.OwnershipFile);
    internal static void RequireOwnedScope(C1SubmissionJournalInspectionRequest request)
    {
        _ = ExistingDirectory(request.NodeDirectory);
        var owner = Owner(request);
        RequireRegularFile(owner);
        if (new FileInfo(owner).Length != EmptyLength)
        { throw C1SubmissionJournalInspectionValidation.Invalid(); }
    }
    internal static string Identity(string directory)
    {
        var path = Path.Combine(directory, ZoneTreePersistenceFormat.IdentityFileName);
        RequireRegularFile(path);
        return path;
    }
    private static string ExistingDirectory(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
        {
            if (!current.Exists || (current.Attributes & FileAttributes.ReparsePoint) != EmptyLength)
            { throw C1SubmissionJournalInspectionValidation.Invalid(); }
        }
        return path;
    }
    private static void RequireRegularFile(string path)
    {
        if (!File.Exists(path) || (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != EmptyLength)
        { throw C1SubmissionJournalInspectionValidation.Invalid(); }
    }
}
