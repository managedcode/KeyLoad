using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnStorageValidation
{
    private const int Empty = 0;
    private const int Missing = -1;
    private const int Step = 1;
    private const int BaseDegreeMultiplier = 2;
    private const int NodeObjectAllowance = 128;

    internal static void Header(PackedAnnStorageHeader header, PackedAnnOptions policy, AnnWorkBudget budget)
    {
        if (header.Version != PackedAnnStorageFrames.CurrentVersion)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, PackedAnnStorageFrames.InvalidSnapshot);
        }
        if (header.Policy != policy || header.Count < Empty || header.Count > policy.MaxRecords)
        {
            throw Errors.Fail(ErrorCode.Corruption, PackedAnnStorageFrames.InvalidSnapshot);
        }
        try
        {
            PackedAnnAdmission.ValidateSpace(header.Space, budget);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Validation)
        {
            throw Errors.Fail(ErrorCode.Corruption, PackedAnnStorageFrames.InvalidSnapshot);
        }
    }

    internal static long Node(PackedAnnStorageNode node, PackedAnnStorageHeader header,
        int ordinal, string? previousId, AnnWorkBudget budget)
    {
        if (node.Ordinal != ordinal || node.Revision <= Empty || node.Level != PackedAnnLevels.For(
            header.Policy.Seed, ordinal, header.Policy.Connections, header.Policy.MaxLevel, budget)
            || node.Vector.Length != header.Space.Dimension || node.Neighbors.Length != node.Level + Step
            || previousId is not null && StringComparer.Ordinal.Compare(previousId, node.Id) >= Empty)
        {
            throw Errors.Fail(ErrorCode.Corruption, PackedAnnStorageFrames.InvalidSnapshot);
        }
        ValidateId(node.Id);
        budget.Charge(node.Vector.Length);
        foreach (var component in node.Vector)
        {
            if (!float.IsFinite(component))
            {
                throw Errors.Fail(ErrorCode.Corruption, PackedAnnStorageFrames.InvalidSnapshot);
            }
        }
        var bytes = checked(NodeObjectAllowance + PackedAnnReservations.String(node.Id.Length)
            + PackedAnnReservations.Array(sizeof(float), node.Vector.Length)
            + PackedAnnReservations.Array(sizeof(long), node.Neighbors.Length));
        for (var layer = Empty; layer < node.Neighbors.Length; layer++)
        {
            ValidateNeighbors(node, header, layer, budget);
            bytes = checked(bytes + PackedAnnReservations.Array(sizeof(int), node.Neighbors[layer].Length));
        }
        return bytes;
    }

    private static void ValidateId(string id)
    {
        try
        {
            JsonData.Identifier(id);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Validation)
        {
            throw Errors.Fail(ErrorCode.Corruption, PackedAnnStorageFrames.InvalidSnapshot);
        }
    }

    private static void ValidateNeighbors(PackedAnnStorageNode node, PackedAnnStorageHeader header,
        int layer, AnnWorkBudget budget)
    {
        var neighbors = node.Neighbors[layer];
        var degree = checked(header.Policy.Connections * (layer == Empty ? BaseDegreeMultiplier : Step));
        if (neighbors.Length > degree)
        {
            throw Errors.Fail(ErrorCode.Corruption, PackedAnnStorageFrames.InvalidSnapshot);
        }
        for (var offset = Empty; offset < neighbors.Length; offset++)
        {
            budget.Charge(Step);
            var target = neighbors[offset];
            if (target < Empty || target >= header.Count || target == node.Ordinal
                || neighbors.AsSpan(Empty, offset).Contains(target))
            {
                throw Errors.Fail(ErrorCode.Corruption, PackedAnnStorageFrames.InvalidSnapshot);
            }
        }
    }

    internal static void Topology(PackedAnnStorageHeader header, PackedAnnStorageNode[] nodes, AnnWorkBudget budget)
    {
        var entry = Missing;
        var maximum = Missing;
        foreach (var node in nodes)
        {
            budget.Check();
            if (node.Level > maximum)
            { entry = node.Ordinal; maximum = node.Level; }
            ValidateTargetLayers(node, nodes, budget);
        }
        if (header.EntryPoint != entry || header.MaximumLevel != maximum)
        {
            throw Errors.Fail(ErrorCode.Corruption, PackedAnnStorageFrames.InvalidSnapshot);
        }
    }

    private static void ValidateTargetLayers(PackedAnnStorageNode node, PackedAnnStorageNode[] nodes, AnnWorkBudget budget)
    {
        for (var layer = Empty; layer < node.Neighbors.Length; layer++)
        {
            foreach (var neighbor in node.Neighbors[layer])
            {
                budget.Charge(Step);
                if (nodes[neighbor].Level < layer)
                {
                    throw Errors.Fail(ErrorCode.Corruption, PackedAnnStorageFrames.InvalidSnapshot);
                }
            }
        }
    }
}
