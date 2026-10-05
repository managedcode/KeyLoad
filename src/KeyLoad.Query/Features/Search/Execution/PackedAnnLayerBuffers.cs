namespace KeyLoad.Query.Features.Search;

internal sealed class PackedAnnLayerBuffers
{
    private readonly bool useVisitBitmap;
    private readonly int[]? marks;
    private readonly ulong[]? visitBitmap;

    internal PackedAnnLayerBuffers(int count, int capacity, bool useBitmap, AnnWorkBudget budget)
    {
        useVisitBitmap = useBitmap;
        var words = checked((count + 63) / 64);
        budget.Charge(checked((long)(useBitmap ? words : count) + (long)capacity * 5));
        marks = useBitmap ? null : new int[count];
        visitBitmap = useBitmap ? new ulong[words] : null;
        Nodes = new int[capacity];
        Scores = new double[capacity];
        BestHeapSlots = new int[capacity];
        BestHeapPositions = new int[capacity];
        WorstHeapSlots = new int[capacity];
    }

    internal int[] Nodes { get; }
    internal double[] Scores { get; }
    internal int[] BestHeapSlots { get; }
    internal int[] BestHeapPositions { get; }
    internal int[] WorstHeapSlots { get; }
    internal int Epoch { get; private set; }
    internal int BestHeapCount { get; set; }
    internal int WorstHeapCount { get; set; }

    internal void BeginVisitPass(AnnWorkBudget budget)
    {
        BestHeapCount = 0;
        WorstHeapCount = 0;
        for (var index = 0; index < BestHeapPositions.Length; index++)
        {
            budget.Charge(1);
            BestHeapPositions[index] = -1;
        }
        if (useVisitBitmap)
        {
            ClearVisitBitmap(budget);
            return;
        }
        if (Epoch == int.MaxValue)
        {
            ClearEpochMarks(budget);
            Epoch = 1;
        }
        else
        {
            Epoch++;
        }
    }

    internal bool IsVisited(int node)
        => useVisitBitmap
            ? (visitBitmap![node >> 6] & (1UL << (node & 63))) != 0
            : marks![node] == Epoch;

    internal void MarkVisited(int node)
    {
        if (useVisitBitmap)
        {
            visitBitmap![node >> 6] |= 1UL << (node & 63);
        }
        else
        {
            marks![node] = Epoch;
        }
    }

    private void ClearVisitBitmap(AnnWorkBudget budget)
    {
        var bits = visitBitmap!;
        for (var index = 0; index < bits.Length; index++)
        {
            budget.Charge(1);
            bits[index] = 0;
        }
    }

    private void ClearEpochMarks(AnnWorkBudget budget)
    {
        var values = marks!;
        for (var index = 0; index < values.Length; index++)
        {
            budget.Charge(1);
            values[index] = 0;
        }
    }
}
