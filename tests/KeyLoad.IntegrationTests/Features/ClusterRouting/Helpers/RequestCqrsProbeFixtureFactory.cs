using KeyLoad.Server;
using static KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsProbeFixtureProtocol;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Creates the fixture's fresh, exclusively owned voter-control roots.</summary>
internal static class RequestCqrsProbeFixtureFactory
{
    internal static RequestCqrsProbeFixture Create(string dataRoot, Guid sessionId, bool captureDiscovery = false)
    {
        if (sessionId == Guid.Empty)
        { throw new ArgumentException(InvalidArm, nameof(sessionId)); }
        var session = sessionId.ToString("N");
        var root = CreateRoot(dataRoot, session);
        var directories = new Dictionary<string, string>(StringComparer.Ordinal);
        var owners = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var failures = new List<Exception>();
        RequestCqrsProbeFixture? fixture = null;
        ServerFailureObserver.Observe(() =>
        {
            CreateNode(root, session, RequestCqrsRf3Protocol.Node1, Node1Origin, directories, owners);
            CreateNode(root, session, RequestCqrsRf3Protocol.Node2, Node2Origin, directories, owners);
            CreateNode(root, session, RequestCqrsRf3Protocol.Node3, Node3Origin, directories, owners);
            fixture = new RequestCqrsProbeFixture(root, session, directories, owners, captureDiscovery);
        }, failures);
        if (failures.Count > 0)
        {
            ServerFailureObserver.Observe(() => RequestCqrsProbeFileStore.DeleteOwnedTree(root, directories, owners,
                requireAllOwners: false), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
        return fixture ?? throw new InvalidOperationException(InvalidControlEntry);
    }

    private static void CreateNode(string root, string session, string node, string voter,
        Dictionary<string, string> directories, Dictionary<string, byte[]> owners)
    {
        var created = RequestCqrsProbeFixtureOperations.PrepareNode(root, session, node, voter);
        directories.Add(node, created.Directory);
        owners.Add(node, created.OwnerBytes);
        RequestCqrsProbeFileStore.WriteAtomic(created.Directory, OwnerFileName, created.OwnerBytes);
    }

    private static string CreateRoot(string dataRoot, string sessionId)
    {
        if (OperatingSystem.IsWindows())
        { throw new PlatformNotSupportedException(PrivatePermissionsUnsupported); }
        var root = Path.Combine(Path.GetTempPath(), RootPrefix + sessionId);
        var fullRoot = Path.GetFullPath(root);
        var fullDataRoot = Path.GetFullPath(dataRoot);
        if (Directory.Exists(fullRoot) || File.Exists(fullRoot)
            || RequestCqrsProbeFileValidation.IsWithin(fullRoot, fullDataRoot)
            || RequestCqrsProbeFileValidation.IsWithin(fullDataRoot, fullRoot))
        { throw new IOException(OwnershipConflict); }
        RequestCqrsProbeFileValidation.EnsureUnixPermissions();
        Directory.CreateDirectory(fullRoot, RequestCqrsProbeFileValidation.PrivateDirectoryMode);
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => RequestCqrsProbeFileValidation.ValidateDirectory(fullRoot), failures);
        if (failures.Count == 0)
        { return fullRoot; }
        ServerFailureObserver.Observe(() => Directory.Delete(fullRoot, recursive: false), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        throw new IOException(InvalidControlEntry);
    }
}
