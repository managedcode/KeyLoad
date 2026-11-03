using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KeyLoad.Server.Search;

/// <summary>Certifies the ordered postings of each hashed term in a sealed authorized generation.</summary>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(TextProtocol.PostingProofAlias)]
internal sealed record TextPostingProof(
    [property: global::Orleans.Id(0)] int DocumentCount,
    [property: global::Orleans.Id(1)] long TotalLength,
    [property: global::Orleans.Id(2)] long UniquePostings,
    [property: global::Orleans.Id(3)] byte[] Terms)
{
    private const int CountOffset = sizeof(ulong);
    private const int DigestOffset = CountOffset + sizeof(long);
    private const int TermBytes = DigestOffset + SHA256.HashSizeInBytes;
    private const int RecordOffset = sizeof(ulong);
    private const int PreviousTokenOffset = RecordOffset + sizeof(ulong);
    private const int BinarySearchDivisor = 2;
    private static readonly byte[] EmptyDigest = SHA256.HashData(ReadOnlySpan<byte>.Empty);

    internal void Validate(long maximumDiskBytes)
    {
        if (DocumentCount < 0 || TotalLength < 0 || UniquePostings < 0
            || UniquePostings > TotalLength || DocumentCount == 0 && TotalLength != 0
            || (UniquePostings == 0) != (TotalLength == 0) || Terms is null
            || Terms.LongLength > maximumDiskBytes || Terms.Length % TermBytes != 0)
        {
            throw TextErrors.Corrupt();
        }

        ulong previous = 0;
        long seen = 0;
        for (var offset = 0; offset < Terms.Length; offset += TermBytes)
        {
            var entry = Terms.AsSpan(offset, TermBytes);
            var token = BinaryPrimitives.ReadUInt64LittleEndian(entry);
            var count = BinaryPrimitives.ReadInt64LittleEndian(entry[CountOffset..]);
            if (offset != 0 && token <= previous || count <= 0 || count > UniquePostings - seen)
            {
                throw TextErrors.Corrupt();
            }
            previous = token;
            seen += count;
        }

        if (seen != UniquePostings)
        {
            throw TextErrors.Corrupt();
        }
    }

    internal bool Matches(TextPostingProof original) => DocumentCount == original.DocumentCount
        && TotalLength == original.TotalLength && UniquePostings == original.UniquePostings
        && Terms.AsSpan().SequenceEqual(original.Terms);

    internal void VerifyToken(ulong token, long count, ReadOnlySpan<byte> sha256)
    {
        var entry = FindToken(token);
        var expectedCount = entry.IsEmpty ? 0 : BinaryPrimitives.ReadInt64LittleEndian(entry[CountOffset..]);
        var expectedDigest = entry.IsEmpty ? EmptyDigest.AsSpan() : entry[DigestOffset..];
        if (count != expectedCount || !CryptographicOperations.FixedTimeEquals(expectedDigest, sha256))
        {
            throw TextErrors.Corrupt();
        }
    }

    internal static void AppendPosting(IncrementalHash digest, ulong token, ulong record, ulong previousToken)
    {
        Span<byte> key = stackalloc byte[TextProtocol.KeyBytes];
        WritePosting(key, token, record, previousToken);
        digest.AppendData(key);
    }

    internal static void WritePosting(Span<byte> destination, ulong token, ulong record, ulong previousToken)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(destination, token);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[RecordOffset..], record);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[PreviousTokenOffset..], previousToken);
    }

    internal static void WriteTerm(MemoryStream output, ulong token, long count, IncrementalHash digest,
        long maximumDiskBytes)
    {
        if (TermBytes > maximumDiskBytes - output.Length)
        {
            throw TextErrors.BoundExceeded();
        }
        Span<byte> entry = stackalloc byte[TermBytes];
        BinaryPrimitives.WriteUInt64LittleEndian(entry, token);
        BinaryPrimitives.WriteInt64LittleEndian(entry[CountOffset..], count);
        if (!digest.TryGetHashAndReset(entry[DigestOffset..], out var written) || written != SHA256.HashSizeInBytes)
        {
            throw TextErrors.Corrupt();
        }
        output.Write(entry);
    }

    private ReadOnlySpan<byte> FindToken(ulong token)
    {
        var start = 0;
        var end = Terms.Length / TermBytes;
        while (start < end)
        {
            var middle = start + (end - start) / BinarySearchDivisor;
            var entry = Terms.AsSpan(middle * TermBytes, TermBytes);
            var candidate = BinaryPrimitives.ReadUInt64LittleEndian(entry);
            if (candidate == token)
            {
                return entry;
            }
            if (candidate < token)
            {
                start = middle + 1;
            }
            else
            {
                end = middle;
            }
        }
        return [];
    }
}
