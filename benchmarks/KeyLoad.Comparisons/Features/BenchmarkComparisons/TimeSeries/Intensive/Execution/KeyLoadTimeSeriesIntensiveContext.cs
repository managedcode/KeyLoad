using System.Collections.Immutable;
using KeyLoad.Client;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed record KeyLoadTimeSeriesIntensivePeer(int Index, string VoterId, Uri Endpoint, KeyLoadClient Client);

internal sealed class KeyLoadTimeSeriesIntensiveContext
{
    internal KeyLoadTimeSeriesIntensiveContext(string runId, PartitionRef partition, string seriesSet,
        Guid incarnation, ImmutableArray<KeyLoadTimeSeriesIntensivePeer> peers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentException.ThrowIfNullOrWhiteSpace(partition.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(partition.DatabaseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(partition.TransactionDomainId);
        ArgumentException.ThrowIfNullOrWhiteSpace(partition.PartitionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(seriesSet);
        if (incarnation == Guid.Empty || peers.IsDefaultOrEmpty
            || peers.Length is not (KeyLoadTimeSeriesIntensiveProtocol.MinimumNodeCount
                or KeyLoadTimeSeriesIntensiveProtocol.MaximumNodeCount))
        {
            throw new ArgumentException(KeyLoadTimeSeriesIntensiveProtocol.InvalidContext, nameof(peers));
        }

        ValidatePeers(peers);
        RunId = runId;
        Partition = partition;
        SeriesSet = seriesSet;
        Incarnation = incarnation;
        Peers = peers;
        ExpectedAtomicPartitionId = partition.AtomicPartitionId;
    }

    internal string RunId { get; }
    internal PartitionRef Partition { get; }
    internal string SeriesSet { get; }
    internal Guid Incarnation { get; }
    internal ImmutableArray<KeyLoadTimeSeriesIntensivePeer> Peers { get; }
    internal string ExpectedAtomicPartitionId { get; }

    private static void ValidatePeers(ImmutableArray<KeyLoadTimeSeriesIntensivePeer> peers)
    {
        const int FirstElementIndex = 0;

        var voterIds = new HashSet<string>(StringComparer.Ordinal);
        var endpoints = new HashSet<string>(StringComparer.Ordinal);
        var clients = new HashSet<KeyLoadClient>(ReferenceEqualityComparer.Instance);
        for (var index = FirstElementIndex; index < peers.Length; index++)
        {
            var peer = peers[index];
            if (peer is null || peer.Index != index + KeyLoadTimeSeriesIntensiveProtocol.FirstPeerIndex
                || string.IsNullOrWhiteSpace(peer.VoterId)
                || peer.Endpoint is null || !peer.Endpoint.IsAbsoluteUri
                || peer.Endpoint.Scheme != Uri.UriSchemeHttp && peer.Endpoint.Scheme != Uri.UriSchemeHttps
                || peer.Client is null || !voterIds.Add(peer.VoterId)
                || !endpoints.Add(peer.Endpoint.AbsoluteUri) || !clients.Add(peer.Client))
            {
                throw new ArgumentException(KeyLoadTimeSeriesIntensiveProtocol.InvalidContext, nameof(peers));
            }
        }
    }
}
