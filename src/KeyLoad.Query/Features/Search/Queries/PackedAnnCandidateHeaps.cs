namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnCandidateHeaps
{
    internal static void AddRetained(PackedAnnLayerBuffers buffers, int slot, AnnWorkBudget budget)
    {
        AddWorst(buffers, slot, budget);
        AddBest(buffers, slot, budget);
    }

    internal static int PopBest(PackedAnnLayerBuffers buffers, AnnWorkBudget budget)
    {
        var count = buffers.BestHeapCount;
        if (count == 0)
        {
            return -1;
        }
        var slot = buffers.BestHeapSlots[0];
        buffers.BestHeapCount = count - 1;
        budget.Charge(1);
        buffers.BestHeapPositions[slot] = -1;
        if (count > 1)
        {
            var last = buffers.BestHeapSlots[count - 1];
            budget.Charge(2);
            buffers.BestHeapSlots[0] = last;
            buffers.BestHeapPositions[last] = 0;
            SiftBestDown(buffers, 0, budget);
        }
        return slot;
    }

    internal static void RemoveBestIfPresent(PackedAnnLayerBuffers buffers, int slot, AnnWorkBudget budget)
    {
        var position = buffers.BestHeapPositions[slot];
        if (position < 0)
        {
            return;
        }
        var lastPosition = buffers.BestHeapCount - 1;
        buffers.BestHeapCount = lastPosition;
        budget.Charge(1);
        buffers.BestHeapPositions[slot] = -1;
        if (position == lastPosition)
        {
            return;
        }
        var movedSlot = buffers.BestHeapSlots[lastPosition];
        budget.Charge(2);
        buffers.BestHeapSlots[position] = movedSlot;
        buffers.BestHeapPositions[movedSlot] = position;
        RestoreBest(buffers, position, budget);
    }

    internal static void AddBest(PackedAnnLayerBuffers buffers, int slot, AnnWorkBudget budget)
    {
        var position = buffers.BestHeapCount;
        buffers.BestHeapCount = position + 1;
        budget.Charge(2);
        buffers.BestHeapSlots[position] = slot;
        buffers.BestHeapPositions[slot] = position;
        SiftBestUp(buffers, position, budget);
    }

    internal static void ReplaceWorstRoot(PackedAnnLayerBuffers buffers, AnnWorkBudget budget)
    {
        if (buffers.WorstHeapCount > 1)
        {
            SiftWorstDown(buffers, 0, budget);
        }
    }

    private static void AddWorst(PackedAnnLayerBuffers buffers, int slot, AnnWorkBudget budget)
    {
        var position = buffers.WorstHeapCount;
        buffers.WorstHeapCount = position + 1;
        budget.Charge(1);
        buffers.WorstHeapSlots[position] = slot;
        SiftWorstUp(buffers, position, budget);
    }

    private static void RestoreBest(PackedAnnLayerBuffers buffers, int position, AnnWorkBudget budget)
    {
        var parent = (position - 1) >> 1;
        if (position > 0 && IsBetter(buffers, buffers.BestHeapSlots[position],
                buffers.BestHeapSlots[parent], budget))
        {
            SiftBestUp(buffers, position, budget);
            return;
        }
        SiftBestDown(buffers, position, budget);
    }

    private static void SiftBestUp(PackedAnnLayerBuffers buffers, int position, AnnWorkBudget budget)
    {
        while (position > 0)
        {
            budget.Check();
            var parent = (position - 1) >> 1;
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
        while (position < count / 2)
        {
            budget.Check();
            var child = position * 2 + 1;
            var right = child + 1;
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
        while (position > 0)
        {
            budget.Check();
            var parent = (position - 1) >> 1;
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
        while (position < count / 2)
        {
            budget.Check();
            var child = position * 2 + 1;
            var right = child + 1;
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
        budget.Charge(1);
        return PackedAnnLayerSearch.Better(buffers.Scores[left], buffers.Nodes[left],
            buffers.Scores[right], buffers.Nodes[right], budget);
    }

    private static bool IsWorse(PackedAnnLayerBuffers buffers, int left, int right, AnnWorkBudget budget)
        => IsBetter(buffers, right, left, budget);

    private static void SwapBest(PackedAnnLayerBuffers buffers, int left, int right, AnnWorkBudget budget)
    {
        var leftSlot = buffers.BestHeapSlots[left];
        var rightSlot = buffers.BestHeapSlots[right];
        budget.Charge(4);
        buffers.BestHeapSlots[left] = rightSlot;
        buffers.BestHeapSlots[right] = leftSlot;
        buffers.BestHeapPositions[leftSlot] = right;
        buffers.BestHeapPositions[rightSlot] = left;
    }

    private static void SwapWorst(PackedAnnLayerBuffers buffers, int left, int right, AnnWorkBudget budget)
    {
        var leftSlot = buffers.WorstHeapSlots[left];
        var rightSlot = buffers.WorstHeapSlots[right];
        budget.Charge(2);
        buffers.WorstHeapSlots[left] = rightSlot;
        buffers.WorstHeapSlots[right] = leftSlot;
    }
}
