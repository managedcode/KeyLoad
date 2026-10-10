using System.Globalization;
using System.Text;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class NativeChecksumProfileCorpus
{
    private static readonly CompositeFormat KeyFormat = CompositeFormat.Parse(NativeChecksumProfileProtocol.CorpusKeyFormat);
    private static readonly CompositeFormat ValueFormat = CompositeFormat.Parse(NativeChecksumProfileProtocol.CorpusValueFormat);

    internal static void Seed(ZoneTreeStore store)
    {
        store.Commit((transaction, _) =>
        {
            for (var index = NativeChecksumProfileProtocol.NoAppends; index < NativeChecksumProfileProtocol.SeedRecords; index++)
            { transaction.Put(Key(index), Value(index)); }
            return true;
        });
        Require(store, NativeChecksumProfileProtocol.NoAppends);
    }

    internal static void Append(ZoneTreeStore store, int append)
    {
        var key = append == NativeChecksumProfileProtocol.OneAppend
            ? NativeChecksumProfileProtocol.FirstCorpusKey : NativeChecksumProfileProtocol.SecondCorpusKey;
        var value = append == NativeChecksumProfileProtocol.OneAppend
            ? NativeChecksumProfileProtocol.FirstCorpusValue : NativeChecksumProfileProtocol.SecondCorpusValue;
        store.Commit((transaction, _) => { transaction.Put(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value)); return true; });
        Require(store, append);
    }

    internal static void Require(ZoneTreeStore store, int appends) => store.Read(view =>
    {
        for (var index = NativeChecksumProfileProtocol.NoAppends; index < NativeChecksumProfileProtocol.SeedRecords; index++)
        { RequireValue(view.ReadOwnedValue(Key(index)), Value(index)); }
        RequireAppend(view.ReadOwnedValue(Encoding.UTF8.GetBytes(NativeChecksumProfileProtocol.FirstCorpusKey)),
            NativeChecksumProfileProtocol.FirstCorpusValue, appends >= NativeChecksumProfileProtocol.OneAppend);
        RequireAppend(view.ReadOwnedValue(Encoding.UTF8.GetBytes(NativeChecksumProfileProtocol.SecondCorpusKey)),
            NativeChecksumProfileProtocol.SecondCorpusValue, appends >= NativeChecksumProfileProtocol.TwoAppends);
        return true;
    });

    private static void RequireAppend(byte[]? actual, string expected, bool present)
    {
        if (present)
        { RequireValue(actual, Encoding.UTF8.GetBytes(expected)); }
        else if (actual is not null)
        { throw new InvalidOperationException(NativeChecksumProfileProtocol.Invalid); }
    }

    private static void RequireValue(byte[]? actual, byte[] expected)
    {
        if (actual is null || !actual.AsSpan().SequenceEqual(expected))
        { throw new InvalidOperationException(NativeChecksumProfileProtocol.Invalid); }
    }

    private static byte[] Key(int index)
        => Encoding.UTF8.GetBytes(string.Format(CultureInfo.InvariantCulture, KeyFormat, index));

    private static byte[] Value(int index)
        => Encoding.UTF8.GetBytes(string.Format(CultureInfo.InvariantCulture, ValueFormat, index)
            + new string(NativeChecksumProfileProtocol.CorpusTail, index % NativeChecksumProfileProtocol.TailLengthModulus));
}
