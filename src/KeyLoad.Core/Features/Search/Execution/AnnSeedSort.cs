namespace KeyLoad.Core.Features.Search;

internal static class AnnSeedSort
{
    private const string DuplicateIdentity = "The canonical ANN seed contains duplicate document identities.";
    internal static void Sort(VectorRecord[] records, int count, AnnSeedWork work)
    {
        const int ChildrenPerHeapParent = 2;
        const int HeapRootIndex = 0;
        const int SingleUnsortedItemCount = 1;
        const int HeapRootIndexForSwap = 0;
        const int HeapRootIndexForSift = 0;
        const int IndexInitialValue = 1;
        const int PreviousItemOffset = 1;
        const int EmptyCompare = 0;

        for (var start = count / ChildrenPerHeapParent; start > HeapRootIndex;)
        {
            start--;
            SiftDown(records, start, count, work);
        }
        for (var end = count; end > SingleUnsortedItemCount;)
        {
            end--;
            Swap(records, HeapRootIndexForSwap, end, work);
            SiftDown(records, HeapRootIndexForSift, end, work);
        }
        for (var index = IndexInitialValue; index < count; index++)
        {
            work.Charge();
            if (Compare(records[index - PreviousItemOffset].DocumentId, records[index].DocumentId, work) == EmptyCompare)
            {
                throw Errors.Fail(ErrorCode.Corruption, DuplicateIdentity);
            }
        }
    }

    private static void SiftDown(VectorRecord[] records, int root, int length, AnnSeedWork work)
    {
        const int ChildrenPerHeapParent = 2;
        const int ChildrenPerParentIndex = 2;
        const int LeftChildOffset = 1;
        const int RightChildOffset = 1;
        const int CompareValidationBoundary = 0;

        while (root < length / ChildrenPerHeapParent)
        {
            work.Charge();
            var child = checked(root * ChildrenPerParentIndex + LeftChildOffset);
            var right = child + RightChildOffset;
            if (right < length && Compare(records[child].DocumentId, records[right].DocumentId, work) < CompareValidationBoundary)
            {
                child = right;
            }
            if (Compare(records[root].DocumentId, records[child].DocumentId, work) >= CompareValidationBoundary)
            {
                return;
            }
            Swap(records, root, child, work);
            root = child;
        }
    }

    private static int Compare(string left, string right, AnnSeedWork work)
    {
        const int IndexInitialValue = 0;
        const int EmptyDifference = 0;

        work.Charge();
        var common = Math.Min(left.Length, right.Length);
        for (var index = IndexInitialValue; index < common; index++)
        {
            work.Charge();
            var difference = left[index] - right[index];
            if (difference != EmptyDifference)
            {
                return difference;
            }
        }
        work.Charge();
        return left.Length.CompareTo(right.Length);
    }

    private static void Swap(VectorRecord[] records, int left, int right, AnnSeedWork work)
    {
        const int SwapWorkUnits = 3;

        work.Charge(SwapWorkUnits);
        (records[left], records[right]) = (records[right], records[left]);
    }
}
