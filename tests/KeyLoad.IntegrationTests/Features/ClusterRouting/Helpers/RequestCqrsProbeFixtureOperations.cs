using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Builds the fixture's independently owned voter directories and initial owner records.</summary>
internal static class RequestCqrsProbeFixtureOperations
{
    internal static (string Directory, byte[] OwnerBytes) PrepareNode(string root, string sessionId,
        string node, string voter, RequestCqrsProbeJson json)
    {
        var bytes = RequestCqrsProbeJsonWriter.Owner(sessionId, voter);
        var owner = json.ReadOwner(bytes);
        if (owner.Version != RequestCqrsProbeFixtureProtocol.Version
            || owner.Kind != RequestCqrsProbeFixtureProtocol.OwnerKind || owner.SessionId != sessionId
            || owner.Voter != voter)
        {
            throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.InvalidArm);
        }
        var directory = CreateNodeDirectory(root, node);
        return (directory, bytes);
    }

    private static string CreateNodeDirectory(string root, string node)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(RequestCqrsProbeFixtureProtocol.PrivatePermissionsUnsupported);
        }
        if (!TwoRf3MembershipProtocol.Nodes.Contains(node, StringComparer.Ordinal))
        {
            throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry);
        }
        var path = Path.Combine(root, node);
        if (Directory.Exists(path) || File.Exists(path))
        {
            throw new IOException(RequestCqrsProbeFixtureProtocol.OwnershipConflict);
        }
        RequestCqrsProbeFileValidation.EnsureUnixPermissions();
        Directory.CreateDirectory(path, RequestCqrsProbeFileValidation.PrivateDirectoryMode);
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => RequestCqrsProbeFileValidation.ValidateDirectory(path), failures);
        if (failures.Count == 0)
        {
            return path;
        }
        ServerFailureObserver.Observe(() => Directory.Delete(path, recursive: false), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry);
    }
    internal static void RetireArmFiles(Guid armId, byte[] bytes,
        IReadOnlyDictionary<string, string> nodeDirectories, IReadOnlyDictionary<string, byte[]> ownerRecords)
    {
        foreach (var voter in RequestCqrsProbeFixtureProtocol.Nodes)
        {
            var directory = nodeDirectories[voter];
            RequestCqrsProbeFileStore.VerifyOwnerFile(directory, ownerRecords[voter]);
            RequestCqrsProbeFileStore.VerifyExactFile(directory, RequestCqrsProbeFileNames.Arm(armId), bytes);
        }
        foreach (var voter in RequestCqrsProbeFixtureProtocol.Nodes)
        {
            RequestCqrsProbeFileStore.DeleteExactFile(nodeDirectories[voter], RequestCqrsProbeFileNames.Arm(armId), bytes);
        }
    }

    internal static async Task VerifyDestinationOwnerOnlyAsync(RequestCqrsProbeFixture fixture,
        bool includeDestination, CancellationToken token)
    {
        if (!includeDestination)
        {
            throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry);
        }
        foreach (var node in TwoRf3MembershipProtocol.Nodes.Skip(TwoRf3MembershipProtocol.MembersPerGroup))
        {
            token.ThrowIfCancellationRequested();
            var owned = fixture.NodeFor(node);
            RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
            var entries = Directory.EnumerateFileSystemEntries(owned.Directory).Take(2).ToArray();
            await Assert.That(entries.SequenceEqual([Path.Combine(owned.Directory, RequestCqrsProbeFixtureProtocol.OwnerFileName)],
                StringComparer.Ordinal)).IsTrue();
        }
    }

}
