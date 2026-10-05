using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Builds the fixture's independently owned voter directories and initial owner records.</summary>
internal static class RequestCqrsProbeFixtureOperations
{
    internal static (string Directory, byte[] OwnerBytes) PrepareNode(string root, string sessionId,
        string node, string voter)
    {
        var bytes = RequestCqrsProbeJsonWriter.Owner(sessionId, voter);
        var owner = RequestCqrsProbeJson.ReadOwner(bytes);
        if (owner.Version != RequestCqrsProbeFixtureProtocol.Version
            || owner.Kind != RequestCqrsProbeFixtureProtocol.OwnerKind || owner.SessionId != sessionId
            || owner.Voter != voter)
        { throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.InvalidArm); }
        var directory = CreateNodeDirectory(root, node);
        return (directory, bytes);
    }

    private static string CreateNodeDirectory(string root, string node)
    {
        if (node is not (RequestCqrsRf3Protocol.Node1 or RequestCqrsRf3Protocol.Node2 or RequestCqrsRf3Protocol.Node3))
        { throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry); }
        var path = Path.Combine(root, node);
        if (Directory.Exists(path) || File.Exists(path))
        { throw new IOException(RequestCqrsProbeFixtureProtocol.OwnershipConflict); }
        RequestCqrsProbeFileValidation.EnsureUnixPermissions();
        Directory.CreateDirectory(path, RequestCqrsProbeFileValidation.PrivateDirectoryMode);
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => RequestCqrsProbeFileValidation.ValidateDirectory(path), failures);
        if (failures.Count == 0)
        { return path; }
        ServerFailureObserver.Observe(() => Directory.Delete(path, recursive: false), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        throw new IOException(RequestCqrsProbeFixtureProtocol.InvalidControlEntry);
    }
}
