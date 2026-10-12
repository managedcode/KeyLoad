using KeyLoad.AppHost.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class NativeCanonicalModelCutFiles(string root)
{
    private const string Prefix = "keyload-canonical-cut-extra-";
    private const int ExtraInventoryEntry = 1;
    internal static IReadOnlyList<string> Nodes { get; } = Array.AsReadOnly(new[]
        { TwoRf3MembershipProtocol.Node4, TwoRf3MembershipProtocol.Node5, TwoRf3MembershipProtocol.Node6 });
    internal string Root { get; } = root;
    internal Dictionary<string, string> Directories { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, byte[]> Owners { get; } = new(StringComparer.Ordinal);

    internal static NativeCanonicalModelCutFiles Create(string dataRoot, RequestCqrsProbeFixture original)
    {
        RequestCqrsProbeFileValidation.EnsureUnixPermissions();
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), Prefix + original.SessionId));
        var data = Path.GetFullPath(dataRoot);
        if (Directory.Exists(path) || File.Exists(path)
            || RequestCqrsProbeFileValidation.IsWithin(path, data)
            || RequestCqrsProbeFileValidation.IsWithin(data, path)
            || RequestCqrsProbeFileValidation.IsWithin(path, original.Root)
            || RequestCqrsProbeFileValidation.IsWithin(original.Root, path))
        { throw new IOException(RequestCqrsProbeFixtureProtocol.OwnershipConflict); }
        CreatePrivateDirectory(path);
        var files = new NativeCanonicalModelCutFiles(path);
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => files.CreateOwners(original), failures);
        if (failures.Count > 0)
        {
            ServerFailureObserver.Observe(() => files.Delete(new Dictionary<Guid, byte[]>(), requireAll: false), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
        return files;
    }

    private void CreateOwners(RequestCqrsProbeFixture original)
    {
        RequestCqrsProbeFileValidation.ValidateDirectory(Root);
        foreach (var node in Nodes)
        {
            var directory = Path.Combine(Root, node);
            var voter = TwoRf3ClusterResources.Origin(node);
            var bytes = RequestCqrsProbeJsonWriter.Owner(original.SessionId, voter);
            var owner = original.Json.ReadOwner(bytes);
            if (owner.SessionId != original.SessionId || owner.Voter != voter)
            { throw new IOException(RequestCqrsProbeFixtureProtocol.OwnerChanged); }
            if (Directory.Exists(directory) || File.Exists(directory))
            { throw new IOException(RequestCqrsProbeFixtureProtocol.OwnershipConflict); }
            CreatePrivateDirectory(directory);
            Directories.Add(node, directory);
            Owners.Add(node, bytes);
            RequestCqrsProbeFileValidation.ValidateDirectory(directory);
            RequestCqrsProbeFileStore.WriteAtomic(directory, RequestCqrsProbeFixtureProtocol.OwnerFileName, bytes);
        }
    }

    internal void RequireInventory(IReadOnlyDictionary<Guid, byte[]> arms, bool requireAll = true)
    {
        RequestCqrsProbeFileValidation.ValidateDirectory(Root);
        if (requireAll && Directories.Count != TwoRf3MembershipProtocol.MembersPerGroup)
        { throw Invalid(); }
        var entries = Directory.EnumerateFileSystemEntries(Root)
            .Take(TwoRf3MembershipProtocol.MembersPerGroup + ExtraInventoryEntry).ToArray();
        if (entries.Length != Directories.Count
            || entries.Any(path => !Directories.Values.Contains(path, StringComparer.Ordinal)))
        { throw Invalid(); }
        foreach (var (node, directory) in Directories)
        {
            if (!Nodes.Contains(node, StringComparer.Ordinal) || directory != Path.Combine(Root, node))
            { throw Invalid(); }
            var expected = arms.ToDictionary(pair => RequestCqrsProbeFileNames.Arm(pair.Key), pair => pair.Value,
                StringComparer.Ordinal);
            var ownerName = RequestCqrsProbeFixtureProtocol.OwnerFileName;
            if (requireAll || File.Exists(Path.Combine(directory, ownerName)))
            { expected.Add(ownerName, Owners[node]); }
            var actual = RequestCqrsProbeFileValidation.ValidateContents(directory);
            if (actual.Length != expected.Count || actual.Any(path => !expected.ContainsKey(Path.GetFileName(path))))
            { throw Invalid(); }
            foreach (var (name, bytes) in expected)
            { RequestCqrsProbeFileStore.VerifyExactFile(directory, name, bytes); }
        }
    }

    internal void Delete(IReadOnlyDictionary<Guid, byte[]> arms, bool requireAll = true)
    {
        RequireInventory(arms, requireAll);
        foreach (var (node, directory) in Directories)
        {
            foreach (var (arm, bytes) in arms)
            { RequestCqrsProbeFileStore.DeleteExactFile(directory, RequestCqrsProbeFileNames.Arm(arm), bytes); }
            var owner = RequestCqrsProbeFixtureProtocol.OwnerFileName;
            if (File.Exists(Path.Combine(directory, owner)))
            { RequestCqrsProbeFileStore.DeleteExactFile(directory, owner, Owners[node]); }
            Directory.Delete(directory, recursive: false);
        }
        Directory.Delete(Root, recursive: false);
    }

    internal static void RequireJoinedOwners(TwoRf3MembershipWave wave)
    {
        if (!wave.applicationDisposed || !wave.nodeLocksReleased)
        { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.UnsettledGates); }
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var directory = Path.Combine(wave.OwnedDataRoot, node);
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(directory, "node.owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(directory, "database", "owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(directory, "replica", "owner.lock"));
        }
    }

    private static void CreatePrivateDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
        { Directory.CreateDirectory(path, RequestCqrsProbeFileValidation.PrivateDirectoryMode); }
        else
        { RequestCqrsProbeFileValidation.EnsureUnixPermissions(); }
    }

    private static IOException Invalid() => new(RequestCqrsProbeFixtureProtocol.InvalidControlEntry);
}
