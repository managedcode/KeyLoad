using System.Collections.Immutable;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.Query.Features.Search;

internal static class AnnPublicEligibility
{
    private const int WordBits = 64;
    private const int WordMask = 63;
    private const int Empty = 0;
    private const int Step = 1;
    private const ulong SetBit = 1;
    private const string Missing = "The native ANN dependency history is unavailable; explicitly rebuild the generation.";

    internal static ulong[] Create(ImmutableArray<VectorRecord> source, AnnSeed caller,
        ImmutableArray<string>? allowed, AnnWorkBudget work)
    {
        work.Charge(checked(((long)source.Length + WordMask) / WordBits * sizeof(ulong)));
        var bitmap = new ulong[(source.Length + WordMask) / WordBits];
        var eligible = Empty;
        for (var index = Empty; index < source.Length; index++)
        {
            work.Check();
            if (eligible == caller.Records.Length)
            { break; }
            var actual = caller.Records[eligible];
            var candidate = source[index];
            work.Charge(checked((long)actual.DocumentId.Length + candidate.DocumentId.Length));
            var order = StringComparer.Ordinal.Compare(candidate.DocumentId, actual.DocumentId);
            if (order > Empty)
            { throw Errors.Fail(ErrorCode.HistoryUnavailable, Missing); }
            if (order != Empty)
            { continue; }
            RequireSame(candidate, actual, work);
            if (Allows(allowed, actual.DocumentId, work))
            { bitmap[index / WordBits] |= SetBit << (index & WordMask); }
            eligible++;
        }
        if (eligible != caller.Records.Length)
        { throw Errors.Fail(ErrorCode.HistoryUnavailable, Missing); }
        work.Check();
        return bitmap;
    }

    private static void RequireSame(VectorRecord expected, VectorRecord actual, AnnWorkBudget work)
    {
        work.Charge(actual.Values.Length);
        if (expected.DocumentRevision != actual.DocumentRevision || expected.Field != actual.Field
            || expected.Space != actual.Space || expected.Values.Length != actual.Values.Length)
        { throw Errors.Fail(ErrorCode.HistoryUnavailable, Missing); }
        for (var index = Empty; index < actual.Values.Length; index++)
        {
            work.Check();
            if (BitConverter.SingleToInt32Bits(expected.Values[index]) != BitConverter.SingleToInt32Bits(actual.Values[index]))
            { throw Errors.Fail(ErrorCode.HistoryUnavailable, Missing); }
        }
    }

    private static bool Allows(ImmutableArray<string>? allowed, string id, AnnWorkBudget work)
    {
        if (allowed is null)
        { return true; }
        foreach (var value in allowed.Value)
        {
            work.Charge(checked((long)value.Length + id.Length + Step));
            if (StringComparer.Ordinal.Equals(value, id))
            { return true; }
        }
        return false;
    }
}
