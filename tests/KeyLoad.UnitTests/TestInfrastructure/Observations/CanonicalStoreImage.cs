using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests;

/// <summary>Complete bounded execution pagination under one canonical store read cut.</summary>
internal static class CanonicalStoreImage
{
    private const int PageRecords = 4_096;
    private const int MaximumPages = 16;
    private const int EqualKeys = 0;

    internal static string[] Bytes(ZoneTreeStore store) => store.Read(Bytes);

    internal static string[] Bytes(IKeyValueView view)
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
                    throw new InvalidOperationException("The execution full-store continuation did not advance.");
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
                throw new InvalidOperationException("The execution full-store page reported continuation without records.");
            }
        }
        throw new InvalidOperationException("The execution full-store image exceeded its bounded page count.");
    }
}
