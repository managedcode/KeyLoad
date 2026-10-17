using KeyLoad.Query.Features.Search;
using static KeyLoad.UnitTests.Features.Search.AnnProviderQualificationProtocol;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnProviderQualificationDamageCodec
{
    internal static byte[] Create(AnnProviderQualificationOwner owner, AnnProviderQualificationState state,
        AnnProviderQualificationDamage damage, CancellationToken token)
    {
        using var source = new MemoryStream(state.OriginalFile, writable: false);
        var budget = AnnProviderQualificationCorpus.Budget(owner, token);
        var header = PackedAnnStorageFrames.Read<PackedAnnStorageHeader>(source, budget);
        var nodes = new PackedAnnStorageNode[header.Count];
        for (var ordinal = FirstNode; ordinal < nodes.Length; ordinal++)
        {
            nodes[ordinal] = PackedAnnStorageFrames.Read<PackedAnnStorageNode>(source, budget);
        }
        header = damage switch
        {
            AnnProviderQualificationDamage.HeaderVersion => header with
            {
                Version = UnsupportedFormat
            },
            AnnProviderQualificationDamage.HeaderEntryPoint => header with
            {
                EntryPoint = CorpusCount
            },
            AnnProviderQualificationDamage.HeaderRetainedBytes => header with
            {
                RetainedBytes = checked(header.RetainedBytes + ReservationDifference)
            },
            _ => header
        };
        nodes[FirstNode] = ChangeNode(nodes[FirstNode], damage);
        using var target = new MemoryStream();
        var options = new PackedAnnStorageOptions();
        PackedAnnStorageFrames.Write(target, header, options, budget);
        foreach (var node in nodes)
        {
            PackedAnnStorageFrames.Write(target, node, options, budget);
        }
        return target.ToArray();
    }

    private static PackedAnnStorageNode ChangeNode(PackedAnnStorageNode node, AnnProviderQualificationDamage damage)
        => damage switch
        {
            AnnProviderQualificationDamage.NodeOrdinal => node with
            {
                Ordinal = OrdinalStep
            },
            AnnProviderQualificationDamage.NodeRevision => node with
            {
                Revision = MissingRevision
            },
            AnnProviderQualificationDamage.NonfiniteVector => node with
            {
                Vector = [float.NaN, EmptyComponent]
            },
            AnnProviderQualificationDamage.SelfNeighbor => WithNeighbors(node, [FirstNode]),
            AnnProviderQualificationDamage.DuplicateNeighbor => WithNeighbors(node, [OtherNode, OtherNode]),
            AnnProviderQualificationDamage.HeaderVersion or AnnProviderQualificationDamage.HeaderEntryPoint
                or AnnProviderQualificationDamage.HeaderRetainedBytes => node,
            _ => throw new InvalidOperationException(InvalidDamage)
        };

    private static PackedAnnStorageNode WithNeighbors(PackedAnnStorageNode node, int[] neighbors)
    {
        var layers = node.Neighbors.Select(layer => layer.ToArray()).ToArray();
        layers[FirstLayer] = neighbors;
        return node with
        {
            Neighbors = layers
        };
    }
}
