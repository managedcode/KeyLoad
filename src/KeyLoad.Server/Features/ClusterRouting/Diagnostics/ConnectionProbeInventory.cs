using System.Text.Json;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class ConnectionProbeInventory
{
    internal static string Name(ConnectionProbeWitness value)
        => Name(value.ArmId, value.RequestId, value.Closed);

    internal static string Name(Guid armId, Guid requestId, bool closed)
        => string.Concat(ConnectionProbeProtocol.Prefix, armId.ToString(RequestCqrsProbeProtocol.SessionIdFormat),
            ConnectionProbeProtocol.Separator, requestId.ToString(RequestCqrsProbeProtocol.SessionIdFormat),
            ConnectionProbeProtocol.Separator, closed ? ConnectionProbeProtocol.Closed : ConnectionProbeProtocol.Active,
            RequestCqrsProbeProtocol.JsonFileSuffix);

    internal static bool IsName(string name)
    {
        if (!name.StartsWith(ConnectionProbeProtocol.Prefix, StringComparison.Ordinal)
            || !name.EndsWith(RequestCqrsProbeProtocol.JsonFileSuffix, StringComparison.Ordinal))
        { return false; }
        var parts = name[ConnectionProbeProtocol.Prefix.Length..^RequestCqrsProbeProtocol.JsonFileSuffix.Length]
            .Split(ConnectionProbeProtocol.Separator, StringSplitOptions.None);
        return parts.Length == ConnectionProbeProtocol.NameParts
            && CanonicalGuid(parts[ConnectionProbeProtocol.ArmPart])
            && CanonicalGuid(parts[ConnectionProbeProtocol.RequestPart])
            && parts[ConnectionProbeProtocol.StatePart] is ConnectionProbeProtocol.Active or ConnectionProbeProtocol.Closed;
    }

    internal static ConnectionProbeWitness Read(ReadOnlySpan<byte> bytes)
    {
        var value = JsonSerializer.Deserialize(bytes, ConnectionProbeJsonContext.Default.ConnectionProbeWitness)
            ?? throw Invalid();
        Require(value);
        return value;
    }

    internal static byte[] Write(ConnectionProbeWitness value)
    {
        Require(value);
        return JsonSerializer.SerializeToUtf8Bytes(value, ConnectionProbeJsonContext.Default.ConnectionProbeWitness);
    }

    internal static void Require(ConnectionProbeWitness value)
    {
        if (value.Version != RequestCqrsProbeProtocol.Version
            || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId)
            || value.ArmId == Guid.Empty || value.RequestId == Guid.Empty || value.ConnectionId == Guid.Empty
            || string.IsNullOrWhiteSpace(value.Voter) || string.IsNullOrWhiteSpace(value.SiloAddress)
            || value.ConnectionGrainType != GrainRoutingProtocol.RequestAlias
            || value.SelectedActivationCount != (value.Closed ? ConnectionProbeProtocol.Absent : ConnectionProbeProtocol.One)
            || value.ClusterConnectionActivationCount < value.SelectedActivationCount
            || GrainId.Parse(value.GrainId, System.Globalization.CultureInfo.InvariantCulture).Type
                != GrainType.Create(value.ConnectionGrainType)
            || ActivationId.FromParsableString(value.ActivationId).IsDefault)
        { throw Invalid(); }
    }

    internal static void RequireMarkers(IReadOnlyList<ConnectionProbeWitness> witnesses,
        IReadOnlyList<RequestCqrsProbeMarkerRecord> markers)
    {
        foreach (var value in witnesses)
        {
            var marker = markers.SingleOrDefault(candidate => candidate.ArmId == value.ArmId
                && candidate.RequestId == value.RequestId && candidate.Phase == RequestCqrsProbePhase.RequestStarted
                && candidate.Outcome == RequestCqrsProbeOutcome.Observed);
            if (marker.SessionId != value.SessionId || marker.CommandId != value.CommandId
                || marker.Voter != value.Voter || marker.SiloAddress != value.SiloAddress)
            { throw Invalid(); }
            if (value.Closed)
            {
                var active = witnesses.SingleOrDefault(candidate => candidate.ArmId == value.ArmId
                    && candidate.RequestId == value.RequestId && !candidate.Closed) ?? throw Invalid();
                if (active.ConnectionId != value.ConnectionId || active.GrainId != value.GrainId
                    || active.ActivationId != value.ActivationId)
                { throw Invalid(); }
            }
        }
    }

    private static bool CanonicalGuid(string text)
        => Guid.TryParseExact(text, RequestCqrsProbeProtocol.SessionIdFormat, out var identity)
            && identity != Guid.Empty && identity.ToString(RequestCqrsProbeProtocol.SessionIdFormat) == text;

    private static InvalidOperationException Invalid() => new(ConnectionProbeProtocol.Invalid);
}
