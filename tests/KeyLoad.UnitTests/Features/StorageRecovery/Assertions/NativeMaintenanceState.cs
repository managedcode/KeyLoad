using System.Text;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class NativeMaintenanceState
{
    internal const int ScanLimit = 16;
    internal static byte[] Key(string value) => Encoding.UTF8.GetBytes(value);
    internal static byte[] Value(string value) => Encoding.UTF8.GetBytes(value);
    internal static string[] Image(IAtomicStore store) => store.Read(view =>
    {
        var page = view.Scan(Array.Empty<byte>(), ScanLimit);
        if (page.HasMore)
        { throw new InvalidOperationException("The literal native maintenance inventory exceeded its bound."); }
        return page.Records.Select(record => Convert.ToHexString(record.Key.Span) + ":" +
            Convert.ToHexString(record.Value.Span)).ToArray();
    });

    internal static async Task LiteralAsync(IAtomicStore store, bool healthy)
    {
        var expected = new[] { "alpha:updated", "gamma:original", healthy ? "omega:healthy" : null }
            .Where(item => item is not null).Select(item => item!.Split(':'))
            .Select(item => Convert.ToHexString(Key(item[0])) + ":" + Convert.ToHexString(Value(item[1]))).ToArray();
        await Assert.That(Image(store)).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    internal static void ExclusiveFiles(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            using var original = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
    }
}
