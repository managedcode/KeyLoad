using System.Security.Cryptography;
using KeyLoad.Core;
using ZoneTree;
using ZoneTree.FullTextSearch;

namespace KeyLoad.Server.Search;

/// <summary>Compares the intended unique postings against one native seal scan without retaining both sets.</summary>
internal sealed class TextPostingProofBuilder : IDisposable
{
    private readonly IncrementalHash keyed = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256,
        RandomNumberGenerator.GetBytes(SHA256.HashSizeInBytes));
    private readonly byte[] expectedFingerprint = new byte[SHA256.HashSizeInBytes];
    private readonly HashSet<(ulong Token, ulong Previous)> recordPostings = [];
    private long totalLength;
    private long expectedCount;

    internal void BeginRecord() => recordPostings.Clear();
    public void Dispose() => keyed.Dispose();

    internal void Observe(ulong token, ulong record, ulong previousToken)
    {
        totalLength = checked(totalLength + 1);
        if (recordPostings.Add((token, previousToken)))
        {
            expectedCount = checked(expectedCount + 1);
            Span<byte> key = stackalloc byte[TextProtocol.KeyBytes];
            TextPostingProof.WritePosting(key, token, record, previousToken);
            Accumulate(expectedFingerprint, key);
        }
    }

    internal TextPostingProof Seal(IZoneTree<CompositeKeyOfTokenRecordPrevious<ulong, ulong>, byte> index,
        int documentCount, long maximumDiskBytes, ReadExecutionBudget budget)
    {
        using var terms = new MemoryStream();
        Span<byte> actualFingerprint = stackalloc byte[SHA256.HashSizeInBytes];
        actualFingerprint.Clear();
        var actualCount = ReadNative(index, terms, actualFingerprint, documentCount, maximumDiskBytes, budget);
        if (actualCount != expectedCount || !CryptographicOperations.FixedTimeEquals(expectedFingerprint, actualFingerprint))
        {
            throw TextErrors.Corrupt();
        }
        return new TextPostingProof(documentCount, totalLength, actualCount, terms.ToArray());
    }

    private long ReadNative(IZoneTree<CompositeKeyOfTokenRecordPrevious<ulong, ulong>, byte> index,
        MemoryStream terms, Span<byte> fingerprint, int documentCount, long maximumDiskBytes, ReadExecutionBudget budget)
    {
        using var iterator = index.CreateIterator(IteratorType.NoRefresh, contributeToTheBlockCache: false);
        using var ordered = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> keyBytes = stackalloc byte[TextProtocol.KeyBytes];
        var comparer = index.Comparer;
        var previous = default(CompositeKeyOfTokenRecordPrevious<ulong, ulong>);
        long count = 0;
        long tokenCount = 0;
        budget.Check();
        iterator.SeekFirst();
        while (MoveNext(iterator, budget))
        {
            var key = iterator.CurrentKey;
            if (key.Record == 0 || key.Record > (ulong)documentCount || iterator.CurrentValue != 0
                || count == expectedCount || count != 0 && comparer.Compare(previous, key) >= 0)
            {
                throw TextErrors.Corrupt();
            }
            if (tokenCount != 0 && key.Token != previous.Token)
            {
                TextPostingProof.WriteTerm(terms, previous.Token, tokenCount, ordered, maximumDiskBytes);
                tokenCount = 0;
            }
            TextPostingProof.WritePosting(keyBytes, key.Token, key.Record, key.PreviousToken);
            Accumulate(fingerprint, keyBytes);
            ordered.AppendData(keyBytes);
            count++;
            tokenCount++;
            previous = key;
        }
        if (tokenCount != 0)
        {
            TextPostingProof.WriteTerm(terms, previous.Token, tokenCount, ordered, maximumDiskBytes);
        }
        budget.Check();
        return count;
    }

    private void Accumulate(Span<byte> sum, ReadOnlySpan<byte> key)
    {
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        keyed.AppendData(key);
        if (!keyed.TryGetHashAndReset(digest, out var written) || written != digest.Length)
        {
            throw TextErrors.Corrupt();
        }
        for (var index = 0; index < digest.Length; index++)
        {
            sum[index] ^= digest[index];
        }
    }

    private static bool MoveNext(IZoneTreeIterator<CompositeKeyOfTokenRecordPrevious<ulong, ulong>, byte> iterator,
        ReadExecutionBudget budget)
    {
        budget.Check();
        return iterator.Next();
    }
}
