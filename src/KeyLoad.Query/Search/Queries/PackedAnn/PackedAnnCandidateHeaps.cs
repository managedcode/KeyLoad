namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnCandidateHeaps
{
    private const int EmptyElementCount = 0;
    private const int MissingHeapPosition = -1;
    private const int FirstElementIndex = 0;
    private const int AdjacentElementOffset = 1;
    private const int SingleWorkUnit = 1;
    private const int MinimumPositiveCount = 1;
    private const int HeapChildCount = 2;
    private const int ParentIndexShift = 1;
    private const int HeapComparisonWork = 4;

    internal static void AddRetained(PackedAnnLayerBuffers buffers, int slot, AnnWorkBudget budget)
    {
        AddWorst(buffers, slot, budget);
        AddBest(buffers, slot, budget);
    }

    internal static int PopBest(PackedAnnLayerBuffers buffers, AnnWorkBudget budget)
    {
        var count = buffers.BestHeapCount;
        if (count == EmptyElementCount)
        {
            return MissingHeapPosition;
        }
        var slot = buffers.BestHeapSlots[FirstElementIndex];
        buffers.BestHeapCount = count - AdjacentElementOffset;
        budget.Charge(SingleWorkUnit);
        buffers.BestHeapPositions[slot] = MissingHeapPosition;
        if (count > MinimumPositiveCount)
        {
            var last = buffers.BestHeapSlots[count - AdjacentElementOffset];
            budget.Charge(HeapChildCount);
            buffers.BestHeapSlots[FirstElementIndex] = last;
            buffers.BestHeapPositions[last] = EmptyElementCount;
            SiftBestDown(buffers, EmptyElementCount, budget);
        }
        return slot;
    }

    internal static void RemoveBestIfPresent(PackedAnnLayerBuffers buffers, int slot, AnnWorkBudget budget)
    {
        var position = buffers.BestHeapPositions[slot];
        if (position < EmptyElementCount)
        {
            return;
        }
        var lastPosition = buffers.BestHeapCount - AdjacentElementOffset;
        buffers.BestHeapCount = lastPosition;
        budget.Charge(SingleWorkUnit);
        buffers.BestHeapPositions[slot] = MissingHeapPosition;
        if (position == lastPosition)
        {
            return;
        }
        var movedSlot = buffers.BestHeapSlots[lastPosition];
        budget.Charge(HeapChildCount);
        buffers.BestHeapSlots[position] = movedSlot;
        buffers.BestHeapPositions[movedSlot] = position;
        RestoreBest(buffers, position, budget);
    }

    internal static void AddBest(PackedAnnLayerBuffers buffers, int slot, AnnWorkBudget budget)
    {
        var position = buffers.BestHeapCount;
        buffers.BestHeapCount = position + AdjacentElementOffset;
        budget.Charge(HeapChildCount);
        buffers.BestHeapSlots[position] = slot;
        buffers.BestHeapPositions[slot] = position;
        SiftBestUp(buffers, position, budget);
    }

    internal static void ReplaceWorstRoot(PackedAnnLayerBuffers buffers, AnnWorkBudget budget)
    {
        if (buffers.WorstHeapCount > MinimumPositiveCount)
        {
            SiftWorstDown(buffers, EmptyElementCount, budget);
        }
    }

    private static void AddWorst(PackedAnnLayerBuffers buffers, int slot, AnnWorkBudget budget)
    {
        var position = buffers.WorstHeapCount;
        buffers.WorstHeapCount = position + AdjacentElementOffset;
        budget.Charge(SingleWorkUnit);
        buffers.WorstHeapSlots[position] = slot;
        SiftWorstUp(buffers, position, budget);
    }

    private static void RestoreBest(PackedAnnLayerBuffers buffers, int position, AnnWorkBudget budget)
    {
        var parent = (position - AdjacentElementOffset) >> ParentIndexShift;
        if (position > EmptyElementCount && IsBetter(buffers, buffers.BestHeapSlots[position],
                buffers.BestHeapSlots[parent], budget))
        {
            SiftBestUp(buffers, position, budget);
            return;
        }
        SiftBestDown(buffers, position, budget);
    }

    private static void SiftBestUp(PackedAnnLayerBuffers buffers, int position, AnnWorkBudget budget)
    {
        while (position > EmptyElementCount)
        {
            var parent = (position - AdjacentElementOffset) >> ParentIndexShift;
            if (!IsBetter(buffers, buffers.BestHeapSlots[position], buffers.BestHeapSlots[parent], budget))
            {
                break;
            }
            SwapBest(buffers, position, parent, budget);
            position = parent;
        }
    }

    private static void SiftBestDown(PackedAnnLayerBuffers buffers, int position, AnnWorkBudget budget)
    {
        var count = buffers.BestHeapCount;
        while (position < count / HeapChildCount)
        {
            var child = position * HeapChildCount + AdjacentElementOffset;
            var right = child + AdjacentElementOffset;
            if (right < count && IsBetter(buffers, buffers.BestHeapSlots[right],
                    buffers.BestHeapSlots[child], budget))
            {
                child = right;
            }
            if (!IsBetter(buffers, buffers.BestHeapSlots[child], buffers.BestHeapSlots[position], budget))
            {
                break;
            }
            SwapBest(buffers, position, child, budget);
            position = child;
        }
    }

    private static void SiftWorstUp(PackedAnnLayerBuffers buffers, int position, AnnWorkBudget budget)
    {
        while (position > EmptyElementCount)
        {
            var parent = (position - AdjacentElementOffset) >> ParentIndexShift;
            if (!IsWorse(buffers, buffers.WorstHeapSlots[position],
                    buffers.WorstHeapSlots[parent], budget))
            {
                break;
            }
            SwapWorst(buffers, position, parent, budget);
            position = parent;
        }
    }

    private static void SiftWorstDown(PackedAnnLayerBuffers buffers, int position, AnnWorkBudget budget)
    {
        var count = buffers.WorstHeapCount;
        while (position < count / HeapChildCount)
        {
            var child = position * HeapChildCount + AdjacentElementOffset;
            var right = child + AdjacentElementOffset;
            if (right < count && IsWorse(buffers, buffers.WorstHeapSlots[right],
                    buffers.WorstHeapSlots[child], budget))
            {
                child = right;
            }
            if (!IsWorse(buffers, buffers.WorstHeapSlots[child], buffers.WorstHeapSlots[position], budget))
            {
                break;
            }
            SwapWorst(buffers, position, child, budget);
            position = child;
        }
    }

    private static bool IsBetter(PackedAnnLayerBuffers buffers, int left, int right, AnnWorkBudget budget)
    {
        budget.Charge(SingleWorkUnit);
        return PackedAnnLayerSearch.Better(buffers.Scores[left], buffers.Nodes[left],
            buffers.Scores[right], buffers.Nodes[right], budget);
    }

    private static bool IsWorse(PackedAnnLayerBuffers buffers, int left, int right, AnnWorkBudget budget)
        => IsBetter(buffers, right, left, budget);

    private static void SwapBest(PackedAnnLayerBuffers buffers, int left, int right, AnnWorkBudget budget)
    {
        var leftSlot = buffers.BestHeapSlots[left];
        var rightSlot = buffers.BestHeapSlots[right];
        budget.Charge(HeapComparisonWork);
        buffers.BestHeapSlots[left] = rightSlot;
        buffers.BestHeapSlots[right] = leftSlot;
        buffers.BestHeapPositions[leftSlot] = right;
        buffers.BestHeapPositions[rightSlot] = left;
    }

    private static void SwapWorst(PackedAnnLayerBuffers buffers, int left, int right, AnnWorkBudget budget)
    {
        var leftSlot = buffers.WorstHeapSlots[left];
        var rightSlot = buffers.WorstHeapSlots[right];
        budget.Charge(HeapChildCount);
        buffers.WorstHeapSlots[left] = rightSlot;
        buffers.WorstHeapSlots[right] = leftSlot;
    }
}
