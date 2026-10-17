using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnStorageRestore
{
    private const int Empty = 0;
    private const int Step = 1;

    internal static PackedAnnState State(PackedAnnStorageHeader header, PackedAnnStorageNode[] nodes,
        IOptions<PackedAnnOptions> configured, PackedAnnStorageOptions storage, long snapshotBytes, AnnWorkBudget budget)
    {
        PackedAnnStorageCodec.RequirePeak(checked(snapshotBytes + PackedAnnReservations.Array(sizeof(long), nodes.Length)), storage);
        var ids = nodes.Select(node => node.Id).ToArray();
        var plan = PackedAnnAdmission.CreateRestored(header.Space, ids, configured.Value, budget);
        if (header.RetainedBytes != plan.RetainedBytes || header.BuildScratchBytes != plan.BuildScratchBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, PackedAnnStorageFrames.InvalidSnapshot);
        }
        PackedAnnStorageCodec.RequirePeak(checked(snapshotBytes + plan.RetainedBytes), storage);
        var levels = nodes.Select(node => node.Level).ToArray();
        var offsets = Offsets(levels, configured.Value.Connections, plan.UpperOffsetLength, budget);
        var graph = new PackedAnnGraph(plan.Count, configured.Value.Connections, levels, offsets, plan.BaseSlots, plan.UpperSlots);
        var vectors = PackedAnnVectors.Restore(plan, budget);
        foreach (var node in nodes)
        {
            RestoreNode(node, graph, vectors, budget);
        }
        return new(header.Space, ids, nodes.Select(node => node.Revision).ToArray(), levels, graph, vectors,
            header.EntryPoint, header.MaximumLevel, configured, plan.RetainedBytes, plan.BuildScratchBytes);
    }

    private static int[] Offsets(byte[] levels, int connections, int length, AnnWorkBudget budget)
    {
        var offsets = new int[length];
        for (var ordinal = Empty; ordinal < levels.Length; ordinal++)
        {
            budget.Charge(Step);
            offsets[ordinal + Step] = checked(offsets[ordinal] + levels[ordinal] * connections);
        }
        return offsets;
    }

    private static void RestoreNode(PackedAnnStorageNode node, PackedAnnGraph graph,
        PackedAnnVectors vectors, AnnWorkBudget budget)
    {
        vectors.RestoreNode(node.Ordinal, node.Vector, budget);
        for (var layer = Empty; layer < node.Neighbors.Length; layer++)
        {
            graph.ReplaceNeighbors(node.Ordinal, layer, node.Neighbors[layer], budget);
        }
    }
}
