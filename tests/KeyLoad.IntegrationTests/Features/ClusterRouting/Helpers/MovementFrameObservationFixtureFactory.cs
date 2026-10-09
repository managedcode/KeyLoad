using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class MovementFrameObservationFixtureFactory
{
    internal static MovementFrameObservationFixture Create(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed)
    {
        if (OperatingSystem.IsWindows())
        { throw new PlatformNotSupportedException(RequestCqrsProbeFixtureProtocol.PrivatePermissionsUnsupported); }
        RequestCqrsProbeFileValidation.EnsureUnixPermissions();
        var session = Guid.NewGuid().ToString(MovementFrameObservationFixtureProtocol.SessionFormat);
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), MovementFrameObservationFixtureProtocol.RootPrefix + session));
        if (File.Exists(root) || Directory.Exists(root)
            || RequestCqrsProbeFileValidation.IsWithin(root, wave.OwnedDataRoot)
            || RequestCqrsProbeFileValidation.IsWithin(wave.OwnedDataRoot, root))
        { throw Invalid(); }
        var target = seed.Directory.Owners.Single(owner => owner.Owner.PhysicalShardId
            == seed.FirstRequest.DestinationPhysicalShardId).Owner;
        if (target.VoterIds.Length != TwoRf3MembershipProtocol.MembersPerGroup)
        { throw Invalid(); }
        var options = IntegrationRoutingOptions.ProbeExecution();
        var json = new MovementFrameObservationJson(options);
        var nodes = new Dictionary<string, MovementFrameObservationNodeFiles>(StringComparer.Ordinal);
        Directory.CreateDirectory(root, RequestCqrsProbeFileValidation.PrivateDirectoryMode);
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            if (OperatingSystem.IsWindows())
            { throw new PlatformNotSupportedException(RequestCqrsProbeFixtureProtocol.PrivatePermissionsUnsupported); }
            foreach (var name in MovementFrameObservationFixtureProtocol.ReceiverNodes)
            {
                var voter = target.VoterIds.Single(value => new Uri(value, UriKind.Absolute).Host == name);
                var path = Path.Combine(root, name);
                if (Directory.Exists(path) || File.Exists(path))
                { throw Invalid(); }
                Directory.CreateDirectory(path, RequestCqrsProbeFileValidation.PrivateDirectoryMode);
                var owner = new MovementFrameObservationOwner(MovementFrameObservationProtocol.Version,
                    MovementFrameObservationProtocol.OwnerKind, session, voter);
                nodes.Add(name, new(path, json.WriteOwner(owner), options));
            }
        }, failures);
        if (failures.Count > PartitionMoveProtocol.EmptyCount)
        {
            foreach (var node in nodes.Values)
            { ServerFailureObserver.Observe(node.DeleteOwner, failures); }
            ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: false), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
        return new(root, session, target, nodes, options);
    }
    private static InvalidOperationException Invalid() => new(MovementFrameObservationFixtureProtocol.Invalid);
}
