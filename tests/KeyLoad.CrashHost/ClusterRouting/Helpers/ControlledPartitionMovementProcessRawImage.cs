using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Complete bounded native pagination under one canonical store read cut.</summary>
internal static class ControlledPartitionMovementProcessRawImage
{
    private const int InitialPageIndex = 0;
    private const string ContinuationDidNotAdvance = "The movement full-store continuation did not advance.";
    private const string KeyValueSeparator = ":";
    private const string ContinuationWithoutRecords = "The movement full-store page reported continuation without records.";
    private const string PageLimitExceeded = "The movement full-store image exceeded its bounded page count.";
    private const int PageRecords = 4_096;
    private const int MaximumPages = 16;
    private const int EqualKeys = 0;

    internal static string[] Bytes(ZoneTreeStore store) => store.Read(view =>
    {
        var image = new List<string>();
        byte[]? after = null;
        for (var pageIndex = InitialPageIndex; pageIndex < MaximumPages; pageIndex++)
        {
            var page = view.Scan([], PageRecords, after);
            foreach (var row in page.Records)
            {
                if (after is not null && row.Key.Span.SequenceCompareTo(after) <= EqualKeys)
                {
                    throw new InvalidOperationException(ContinuationDidNotAdvance);
                }
                image.Add(Convert.ToHexString(row.Key.Span) + KeyValueSeparator + Convert.ToHexString(row.Value.Span));
                after = row.Key.ToArray();
            }
            if (!page.HasMore)
            {
                return image.ToArray();
            }
            if (page.Records.IsEmpty)
            {
                throw new InvalidOperationException(ContinuationWithoutRecords);
            }
        }
        throw new InvalidOperationException(PageLimitExceeded);
    });
}
