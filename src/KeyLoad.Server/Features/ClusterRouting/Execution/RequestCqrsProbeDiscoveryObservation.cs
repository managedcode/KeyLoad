using KeyLoad.Orleans;
using KeyLoad.Replication;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeDiscoveryObservation
{
    internal static ValueTask Record(RequestCqrsProbeFiles files, ReplicaConfiguration replica,
        string voterId, ReplicaDiscoveryObservation observation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observation);
        cancellationToken.ThrowIfCancellationRequested();
        if (!replica.VoterIds.Contains(voterId, StringComparer.Ordinal) || voterId == replica.LocalId
            || observation.ProtocolCompatible)
        { throw new InvalidOperationException(RequestCqrsProbeProtocol.InvalidFiles); }
        files.WriteDiscovery(new(RequestCqrsProbeProtocol.Version, RequestCqrsProbeProtocol.DiscoveryKind,
            files.SessionId, replica.LocalId, voterId, observation.ApplicationRpcVersion,
            observation.PeerEnvelopeVersion, observation.TransportReady, observation.ProtocolCompatible));
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

}
