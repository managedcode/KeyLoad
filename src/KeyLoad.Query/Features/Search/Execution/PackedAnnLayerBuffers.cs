namespace KeyLoad.Query.Features.Search;

internal sealed class PackedAnnLayerBuffers
{
    private const int BitmapRemainderMask = 63;
    private const int BitmapWordBits = 64;
    private const int LayerScratchArrayCount = 5;
    private const int EmptyElementCount = 0;
    private const int FirstElementIndex = 0;
    private const int SingleWorkUnit = 1;
    private const int UnassignedHeapPosition = -1;
    private const int AdjacentElementOffset = 1;
    private const int BitmapWordShift = 6;
    private const ulong LowestBitmapBit = 1UL;

    private readonly bool useVisitBitmap;
    private readonly int[]? marks;
    private readonly ulong[]? visitBitmap;

    internal PackedAnnLayerBuffers(int count, int capacity, bool useBitmap, AnnWorkBudget budget)
    {
        useVisitBitmap = useBitmap;
        var words = checked((count + BitmapRemainderMask) / BitmapWordBits);
        budget.Charge(checked((long)(useBitmap ? words : count) + (long)capacity * LayerScratchArrayCount));
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
        BestHeapCount = EmptyElementCount;
        WorstHeapCount = EmptyElementCount;
        for (var index = FirstElementIndex; index < BestHeapPositions.Length; index++)
        {
            budget.Charge(SingleWorkUnit);
            BestHeapPositions[index] = UnassignedHeapPosition;
        }
        if (useVisitBitmap)
        {
            ClearVisitBitmap(budget);
            return;
        }
        if (Epoch == int.MaxValue)
        {
            ClearEpochMarks(budget);
            Epoch = AdjacentElementOffset;
        }
        else
        {
            Epoch++;
        }
    }

    internal bool IsVisited(int node)
        => useVisitBitmap
            ? (visitBitmap![node >> BitmapWordShift] & (LowestBitmapBit << (node & BitmapRemainderMask))) != EmptyElementCount
            : marks![node] == Epoch;

    internal void MarkVisited(int node)
    {
        if (useVisitBitmap)
        {
            visitBitmap![node >> BitmapWordShift] |= LowestBitmapBit << (node & BitmapRemainderMask);
        }
        else
        {
            marks![node] = Epoch;
        }
    }

    private void ClearVisitBitmap(AnnWorkBudget budget)
    {
        var bits = visitBitmap!;
        for (var index = FirstElementIndex; index < bits.Length; index++)
        {
            budget.Charge(SingleWorkUnit);
            bits[index] = EmptyElementCount;
        }
    }

    private void ClearEpochMarks(AnnWorkBudget budget)
    {
        var values = marks!;
        for (var index = FirstElementIndex; index < values.Length; index++)
        {
            budget.Charge(SingleWorkUnit);
            values[index] = EmptyElementCount;
        }
    }
}
