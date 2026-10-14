using System.Collections.Immutable;
using System.Text;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Selects only one bounded current actual native generation for a canonical voter name.</summary>
internal sealed class ReplicaNativeDiscoveryMembership(ISiloStatusOracle oracle,
    IOptions<OrleansMembershipOptions> options)
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal SiloAddress? Resolve(string voter)
    {
        var active = oracle.GetApproximateSiloStatuses(onlyActive: true);
        if (active.Count > options.Value.MaximumRows)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaTransportProtocol.InvalidDiscovery);
        }
        SiloAddress? selected = null;
        foreach (var entry in active)
        {
            if (entry.Value != SiloStatus.Active || !oracle.TryGetSiloName(entry.Key, out var name)
                || !string.Equals(name, voter, StringComparison.Ordinal))
            {
                continue;
            }
            if (selected is not null)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaTransportProtocol.InvalidDiscovery);
            }
            selected = entry.Key;
        }
        return selected;
    }

    internal bool IsCurrent(string voter, SiloAddress address)
        => Resolve(voter) is { } actual && actual.Equals(address)
            && oracle.GetApproximateSiloStatus(address) == SiloStatus.Active
            && oracle.TryGetSiloName(address, out var name)
            && string.Equals(name, voter, StringComparison.Ordinal);

    internal ImmutableArray<ReplicaNativeMembershipEntry> ReadActive()
    {
        var active = oracle.GetApproximateSiloStatuses(onlyActive: true);
        if (active.Count > options.Value.MaximumRows)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaTransportProtocol.InvalidDiscovery);
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<(SiloAddress Address, ReplicaNativeMembershipEntry Entry)>(active.Count);
        foreach (var item in active)
        {
            if (item.Value != SiloStatus.Active || !oracle.TryGetSiloName(item.Key, out var name)
                || !Bounded(name) || !names.Add(name!))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaTransportProtocol.InvalidDiscovery);
            }
            var address = item.Key.ToParsableString();
            if (!Bounded(address))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaTransportProtocol.InvalidDiscovery);
            }
            rows.Add((item.Key, new(name!, address)));
        }
        var current = oracle.GetApproximateSiloStatuses(onlyActive: true);
        if (current.Count > options.Value.MaximumRows)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaTransportProtocol.InvalidDiscovery);
        }
        if (current.Count != rows.Count)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaTransportProtocol.InvalidDiscovery);
        }
        foreach (var row in rows)
        {
            if (!current.TryGetValue(row.Address, out var status) || status != SiloStatus.Active
                || oracle.GetApproximateSiloStatus(row.Address) != SiloStatus.Active
                || !oracle.TryGetSiloName(row.Address, out var name)
                || !string.Equals(name, row.Entry.SiloName, StringComparison.Ordinal)
                || !string.Equals(row.Address.ToParsableString(), row.Entry.SiloAddress, StringComparison.Ordinal))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaTransportProtocol.InvalidDiscovery);
            }
        }
        return rows.OrderBy(row => row.Entry.SiloName, StringComparer.Ordinal)
            .Select(row => row.Entry).ToImmutableArray();
    }

    private bool Bounded(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        try
        {
            return Utf8.GetByteCount(value) <= options.Value.MaximumAddressBytes;
        }
        catch (EncoderFallbackException) { return false; }
    }
}
