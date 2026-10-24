using KeyLoad.Storage;

namespace KeyLoad.UnitTests.StorageRecovery;

internal static class PartialCurrentNodeLayoutState
{
    private const int MaximumRecords = 4_096;
    private const string Overflow = "The bounded current partial-layout operation overflowed.";
    private const string Separator = ":";

    internal static string[] Rows(IAtomicStore store) => store.Read(view =>
    {
        var page = view.Scan([], MaximumRecords);
        if (page.HasMore)
        {
            throw new InvalidOperationException(Overflow);
        }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span)
            + Separator + Convert.ToHexString(row.Value.Span)).Order(StringComparer.Ordinal).ToArray();
    });
}
