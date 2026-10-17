using System.Security.Cryptography;

namespace KeyLoad;

/// <summary>A count and commutative XOR of domain-separated native authority record hashes.</summary>
internal sealed record ResourceRetirementAuthoritySummary(long Records, ReadOnlyMemory<byte> Digest)
{
    internal const int DigestBytes = 32;
    private const long EmptyRecords = 0;
    private const string Invalid = "The native retirement authority accumulator is invalid.";

    internal void RequireValid()
    {
        if (Records < EmptyRecords || Digest.Length != DigestBytes
            || Records == EmptyRecords && !Digest.Span.SequenceEqual(new byte[DigestBytes]))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
    }

    internal bool Same(ResourceRetirementAuthoritySummary other)
    {
        RequireValid();
        other.RequireValid();
        return Records == other.Records && CryptographicOperations.FixedTimeEquals(Digest.Span, other.Digest.Span);
    }
}
