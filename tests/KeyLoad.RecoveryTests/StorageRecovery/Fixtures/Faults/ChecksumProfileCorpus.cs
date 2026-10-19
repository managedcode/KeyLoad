using System.Globalization;
using System.Text;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal static class ChecksumProfileCorpus
{
    private static readonly CompositeFormat KeyFormat = CompositeFormat.Parse(ChecksumProfileProtocol.CorpusKeyFormat);
    private static readonly CompositeFormat ValueFormat = CompositeFormat.Parse(ChecksumProfileProtocol.CorpusValueFormat);

    internal static void Seed(ZoneTreeStore store)
    {
        store.Commit((transaction, _) =>
        {
            for (var index = ChecksumProfileProtocol.NoAppends; index < ChecksumProfileProtocol.SeedRecords; index++)
            {
                transaction.Put(Key(index), Value(index));
            }
            return true;
        });
        Require(store, ChecksumProfileProtocol.NoAppends);
    }

    internal static void Append(ZoneTreeStore store, int append)
    {
        var key = append == ChecksumProfileProtocol.OneAppend
            ? ChecksumProfileProtocol.FirstCorpusKey : ChecksumProfileProtocol.SecondCorpusKey;
        var value = append == ChecksumProfileProtocol.OneAppend
            ? ChecksumProfileProtocol.FirstCorpusValue : ChecksumProfileProtocol.SecondCorpusValue;
        store.Commit((transaction, _) => { transaction.Put(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value)); return true; });
        Require(store, append);
    }

    internal static void Require(ZoneTreeStore store, int appends) => store.Read(view =>
    {
        for (var index = ChecksumProfileProtocol.NoAppends; index < ChecksumProfileProtocol.SeedRecords; index++)
        {
            RequireValue(view.ReadOwnedValue(Key(index)), Value(index));
        }
        RequireAppend(view.ReadOwnedValue(Encoding.UTF8.GetBytes(ChecksumProfileProtocol.FirstCorpusKey)),
            ChecksumProfileProtocol.FirstCorpusValue, appends >= ChecksumProfileProtocol.OneAppend);
        RequireAppend(view.ReadOwnedValue(Encoding.UTF8.GetBytes(ChecksumProfileProtocol.SecondCorpusKey)),
            ChecksumProfileProtocol.SecondCorpusValue, appends >= ChecksumProfileProtocol.TwoAppends);
        return true;
    });

    private static void RequireAppend(byte[]? actual, string expected, bool present)
    {
        if (present)
        {
            RequireValue(actual, Encoding.UTF8.GetBytes(expected));
        }
        else if (actual is not null)
        {
            throw new InvalidOperationException(ChecksumProfileProtocol.Invalid);
        }
    }

    private static void RequireValue(byte[]? actual, byte[] expected)
    {
        if (actual is null || !actual.AsSpan().SequenceEqual(expected))
        {
            throw new InvalidOperationException(ChecksumProfileProtocol.Invalid);
        }
    }

    private static byte[] Key(int index)
        => Encoding.UTF8.GetBytes(string.Format(CultureInfo.InvariantCulture, KeyFormat, index));

    private static byte[] Value(int index)
        => Encoding.UTF8.GetBytes(string.Format(CultureInfo.InvariantCulture, ValueFormat, index)
            + new string(ChecksumProfileProtocol.CorpusTail, index % ChecksumProfileProtocol.TailLengthModulus));
}
