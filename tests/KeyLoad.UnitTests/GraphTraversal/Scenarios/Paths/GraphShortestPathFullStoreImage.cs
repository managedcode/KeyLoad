using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

/// <summary>Complete bounded native pagination under one canonical store read cut.</summary>
internal static class GraphShortestPathFullStoreImage
{
    private const int PageRecords = 4_096;
    private const int MaximumPages = 16;
    private const int EqualKeys = 0;

    internal static string[] Bytes(ZoneTreeStore store) => store.Read(view =>
    {
        var image = new List<string>();
        byte[]? after = null;
        for (var pageIndex = 0; pageIndex < MaximumPages; pageIndex++)
        {
            var page = view.Scan([], PageRecords, after);
            foreach (var row in page.Records)
            {
                if (after is not null && row.Key.Span.SequenceCompareTo(after) <= EqualKeys)
                {
                    throw new InvalidOperationException("The native full-store continuation did not advance.");
                }
                image.Add(Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span));
                after = row.Key.ToArray();
            }
            if (!page.HasMore)
            {
                return image.ToArray();
            }
            if (page.Records.IsEmpty)
            {
                throw new InvalidOperationException("The native full-store page reported continuation without records.");
            }
        }
        throw new InvalidOperationException("The native full-store image exceeded its bounded page count.");
    });
}
