namespace KeyLoad.Query.Features.Search;

internal sealed class PackedAnnGraph
{
    private const string DegreeExceeded = "The ANN adjacency exceeds its admitted degree.";
    private const string InvalidAddress = "The ANN adjacency address is invalid.";
    // Node ordinals stay below 2^23; the first slot packs its count above the ordinal.
    private const int NeighborBits = 23;
    private const int NeighborMask = (1 << NeighborBits) - 1;
    private const int MaximumEncodedDegree = 1 << 8;
    private readonly int[] baseEdges;
    private readonly int[] upperOffsets;
    private readonly int[] upperEdges;
    private readonly byte[] levels;
    private readonly int count;
    private readonly int connections;

    internal PackedAnnGraph(int count, int connections, byte[] levels, int[] upperOffsets,
        int baseSlots, int upperSlots)
    {
        this.count = count;
        this.connections = connections;
        this.levels = levels;
        this.upperOffsets = upperOffsets;
        baseEdges = new int[baseSlots];
        upperEdges = new int[upperSlots];
    }

    internal int Degree(int layer) => layer == 0 ? connections * 2 : connections;
    internal int Connections => connections;

    internal int Neighbor(int node, int layer, int offset)
    {
        var slot = Slot(node, layer, offset);
        var encoded = layer == 0 ? baseEdges[slot] : upperEdges[slot];
        if (offset == 0)
        {
            encoded &= NeighborMask;
        }
        return encoded == 0 ? -1 : encoded - 1;
    }

    internal int NeighborCount(int node, int layer, AnnWorkBudget budget)
    {
        budget.Charge(1);
        var firstSlot = Slot(node, layer, 0);
        var encoded = layer == 0 ? baseEdges[firstSlot] : upperEdges[firstSlot];
        return encoded >> NeighborBits;
    }

    internal void ReplaceNeighbors(int node, int layer, ReadOnlySpan<int> neighbors, AnnWorkBudget budget)
    {
        var degree = Degree(layer);
        if (neighbors.Length > degree)
        {
            throw new InvalidOperationException(DegreeExceeded);
        }
        var start = Slot(node, layer, 0);
        budget.Charge(degree);
        if (layer == 0)
        {
            Array.Clear(baseEdges, start, degree);
        }
        else
        {
            Array.Clear(upperEdges, start, degree);
        }
        if (neighbors.Length >= MaximumEncodedDegree)
        {
            throw new InvalidOperationException(DegreeExceeded);
        }
        for (var offset = 0; offset < neighbors.Length; offset++)
        {
            budget.Charge(1);
            var neighbor = neighbors[offset];
            if ((uint)neighbor >= (uint)count || neighbor >= NeighborMask)
            {
                throw new InvalidOperationException(InvalidAddress);
            }
            var encoded = offset == 0
                ? checked((int)(((uint)neighbors.Length << NeighborBits) | (uint)(neighbor + 1)))
                : checked(neighbor + 1);
            if (layer == 0)
            {
                baseEdges[start + offset] = encoded;
            }
            else
            {
                upperEdges[start + offset] = encoded;
            }
        }
    }

    private int Slot(int node, int layer, int offset)
    {
        var degree = Degree(layer);
        if ((uint)node >= (uint)count || (uint)offset >= (uint)degree || layer < 0
            || layer > levels[node])
        {
            throw new InvalidOperationException(InvalidAddress);
        }
        if (layer == 0)
        {
            return checked(node * degree + offset);
        }
        var first = upperOffsets[node];
        var end = upperOffsets[node + 1];
        var nodeSlots = checked((int)levels[node] * connections);
        if (end - first != nodeSlots)
        {
            throw new InvalidOperationException(InvalidAddress);
        }
        return checked(first + (layer - 1) * connections + offset);
    }
}
