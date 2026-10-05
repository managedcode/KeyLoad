namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Validates the fixed private evidence slots against the actual ordered voter identities.</summary>
internal sealed class RequestCqrsProbeDiscoveryPolicy(IReadOnlyList<string> voterIds, string observerId, bool enabled)
{
    internal void Validate(string sessionId, string observer, RequestCqrsProbeDiscoveryRecord record)
    {
        if (!enabled || record.SessionId != sessionId || record.ObserverVoterId != observer
            || !voterIds.Contains(observer, StringComparer.Ordinal)
            || !voterIds.Contains(record.PeerVoterId, StringComparer.Ordinal)
            || observer == record.PeerVoterId || record.ProtocolCompatible)
        { throw Invalid(); }
    }

    internal static void ValidateInventory(IReadOnlyList<RequestCqrsProbeDiscoveryRecord> records)
    {
        if (records.Count > RequestCqrsProbeProtocol.MaximumDiscoveryRecords
            || records.Select(record => record.PeerVoterId).Distinct(StringComparer.Ordinal).Count() != records.Count)
        { throw Invalid(); }
    }

    internal static void ValidateWrite(IReadOnlyList<RequestCqrsProbeDiscoveryRecord> records,
        RequestCqrsProbeDiscoveryRecord candidate)
    {
        var existing = records.SingleOrDefault(record => record.PeerVoterId == candidate.PeerVoterId);
        if (existing != default && (existing.ApplicationRpcVersion != candidate.ApplicationRpcVersion
            || existing.PeerEnvelopeVersion != candidate.PeerEnvelopeVersion
            || existing.ProtocolCompatible != candidate.ProtocolCompatible))
        { throw Invalid(); }
    }

    internal int GetSlot(string peer)
    {
        var slot = 0;
        foreach (var voter in voterIds)
        {
            if (voter == observerId)
            { continue; }
            if (voter == peer)
            { return slot; }
            slot++;
        }
        throw Invalid();
    }

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidFiles);
}
