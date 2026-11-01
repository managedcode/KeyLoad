using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests;

/// <summary>Complete bounded execution pagination under one canonical store read cut.</summary>
/// <remarks>The image leaves out the store's own position record; callers assert <c>Position</c> explicitly.</remarks>
internal static class CanonicalStoreImage
{
    private const int PageRecords = 4_096;
    private const int MaximumPages = 16;

    /// <summary>Whether a key is the position record that every store commit rewrites.</summary>
    internal static bool IsStorePosition(ReadOnlySpan<byte> key) => ProcessRecoveryTests.StorePositionRecord.Is(key);

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
                if (after is not null && row.Key.Span.SequenceCompareTo(after) <= 0)
                {
                    throw new InvalidOperationException("The execution full-store continuation did not advance.");
                }
                if (!IsStorePosition(row.Key.Span))
                {
                    image.Add(Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span));
                }
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
