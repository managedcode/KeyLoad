namespace KeyLoad.Core.Features.Search;

internal static class AnnSeedSort
{
    private const string DuplicateIdentity = "The canonical ANN seed contains duplicate document identities.";
    internal static void Sort(VectorRecord[] records, int count, AnnSeedWork work)
    {
        for (var start = count / 2; start > 0;)
        {
            start--;
            SiftDown(records, start, count, work);
        }
        for (var end = count; end > 1;)
        {
            end--;
            Swap(records, 0, end, work);
            SiftDown(records, 0, end, work);
        }
        for (var index = 1; index < count; index++)
        {
            work.Charge();
            if (Compare(records[index - 1].DocumentId, records[index].DocumentId, work) == 0)
            {
                throw Errors.Fail(ErrorCode.Corruption, DuplicateIdentity);
            }
        }
    }

    private static void SiftDown(VectorRecord[] records, int root, int length, AnnSeedWork work)
    {
        while (root < length / 2)
        {
            work.Charge();
            var child = checked(root * 2 + 1);
            var right = child + 1;
            if (right < length && Compare(records[child].DocumentId, records[right].DocumentId, work) < 0)
            {
                child = right;
            }
            if (Compare(records[root].DocumentId, records[child].DocumentId, work) >= 0)
            {
                return;
            }
            Swap(records, root, child, work);
            root = child;
        }
    }

    private static int Compare(string left, string right, AnnSeedWork work)
    {
        work.Charge();
        var common = Math.Min(left.Length, right.Length);
        for (var index = 0; index < common; index++)
        {
            work.Charge();
            var difference = left[index] - right[index];
            if (difference != 0)
            {
                return difference;
            }
        }
        work.Charge();
        return left.Length.CompareTo(right.Length);
    }

    private static void Swap(VectorRecord[] records, int left, int right, AnnSeedWork work)
    {
        work.Charge(3);
        (records[left], records[right]) = (records[right], records[left]);
    }
}
