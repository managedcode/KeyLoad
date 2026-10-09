using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Owns one configured peer key under distinct native movement request/reply purposes.</summary>
internal sealed partial class PartitionMovementMac : IDisposable
{
    private const string ReceiverIssueCommandPurpose = "keyload-partition-movement-receiver-issue-command-v1";
    private const string ReceiverIssueQueryPurpose = "keyload-partition-movement-receiver-issue-query-v1";
    private static readonly byte[] ReceiverIssueCommandDomain = Encoding.ASCII.GetBytes(ReceiverIssueCommandPurpose);
    private static readonly byte[] ReceiverIssueQueryDomain = Encoding.ASCII.GetBytes(ReceiverIssueQueryPurpose);
    private const string ReceiverIssueSourcePurpose = "keyload-partition-movement-receiver-issue-source-v1";
    private const string ReceiverIssueReplyPurpose = "keyload-partition-movement-receiver-issue-reply-v1";
    private static readonly byte[] ReceiverIssueSourceDomain = Encoding.ASCII.GetBytes(ReceiverIssueSourcePurpose);
    private static readonly byte[] ReceiverIssueReplyDomain = Encoding.ASCII.GetBytes(ReceiverIssueReplyPurpose);
    private const string RequestPurpose = "keyload-partition-movement-request-v1";
    private const string OutcomeQueryPurpose = "keyload-partition-movement-outcome-query-v1";
    private const string OutcomeReplyPurpose = "keyload-partition-movement-outcome-reply-v1";
    private const string TransferAuthorityPurpose = "keyload-partition-movement-transfer-authority-reply-v1";
    private const string TransferDataRequestPurpose = "keyload-partition-movement-transfer-data-request-v1";
    private const string TransferDataReplyPurpose = "keyload-partition-movement-transfer-data-reply-v1";
    private const string ReplyPurpose = "keyload-partition-movement-reply-v1";
    private const int KeyBytes = 32;
    private const int SignatureCharacters = 64;
    private const int MinimumBodyBytes = 1;
    private const int Active = 0;
    private const int Disposed = 1;
    private const string InvalidProof = "The partition movement peer proof is invalid.";
    private static readonly byte[] RequestDomain = Encoding.ASCII.GetBytes(RequestPurpose);
    private static readonly byte[] ReplyDomain = Encoding.ASCII.GetBytes(ReplyPurpose);
    private static readonly byte[] OutcomeQueryDomain = Encoding.ASCII.GetBytes(OutcomeQueryPurpose);
    private static readonly byte[] OutcomeReplyDomain = Encoding.ASCII.GetBytes(OutcomeReplyPurpose);
    private static readonly byte[] TransferAuthorityDomain = Encoding.ASCII.GetBytes(TransferAuthorityPurpose);
    private static readonly byte[] TransferDataRequestDomain = Encoding.ASCII.GetBytes(TransferDataRequestPurpose);
    private static readonly byte[] TransferDataReplyDomain = Encoding.ASCII.GetBytes(TransferDataReplyPurpose);
    private readonly byte[] key;
    private readonly int maximumBytes;
    private int disposed;

    internal PartitionMovementMac(ReadOnlySpan<byte> configuredKey, int maximumBytes)
    {
        if (configuredKey.Length != KeyBytes || maximumBytes < MinimumBodyBytes)
        { throw Errors.Fail(ErrorCode.Validation, InvalidProof); }
        this.maximumBytes = maximumBytes;
        key = configuredKey.ToArray();
    }

    internal string Sign(ReadOnlySpan<byte> originalBody, bool reply)
        => Convert.ToHexStringLower(Digest(originalBody, reply));

    internal bool Verify(ReadOnlySpan<byte> originalBody, string signature, bool reply)
    {
        if (signature.Length != SignatureCharacters
            || signature.Any(character => !char.IsAsciiHexDigitLower(character)))
        { return false; }
        var supplied = Convert.FromHexString(signature);
        var expected = Digest(originalBody, reply);
        return CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    internal string SignOutcome(ReadOnlySpan<byte> originalBody, bool reply)
        => Convert.ToHexStringLower(DigestDomain(originalBody, reply ? OutcomeReplyDomain : OutcomeQueryDomain));

    internal bool VerifyOutcome(ReadOnlySpan<byte> originalBody, string signature, bool reply)
    {
        if (signature.Length != SignatureCharacters
            || signature.Any(character => !char.IsAsciiHexDigitLower(character)))
        { return false; }
        var supplied = Convert.FromHexString(signature);
        var expected = DigestDomain(originalBody, reply ? OutcomeReplyDomain : OutcomeQueryDomain);
        return CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    internal string SignTransferAuthority(ReadOnlySpan<byte> originalBody)
        => Convert.ToHexStringLower(DigestDomain(originalBody, TransferAuthorityDomain));

    internal bool VerifyTransferAuthority(ReadOnlySpan<byte> originalBody, string signature)
    {
        if (signature.Length != SignatureCharacters
            || signature.Any(character => !char.IsAsciiHexDigitLower(character)))
        { return false; }
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(signature),
            DigestDomain(originalBody, TransferAuthorityDomain));
    }

    internal string SignTransferData(ReadOnlySpan<byte> originalBody, bool reply)
        => Convert.ToHexStringLower(DigestDomain(originalBody, reply ? TransferDataReplyDomain : TransferDataRequestDomain));

    internal bool VerifyTransferData(ReadOnlySpan<byte> originalBody, string signature, bool reply)
    {
        if (signature.Length != SignatureCharacters
            || signature.Any(character => !char.IsAsciiHexDigitLower(character)))
        { return false; }
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(signature),
            DigestDomain(originalBody, reply ? TransferDataReplyDomain : TransferDataRequestDomain));
    }

    internal string SignReceiverIssue(ReadOnlySpan<byte> originalBody, bool source)
        => Convert.ToHexStringLower(DigestDomain(originalBody, source ? ReceiverIssueSourceDomain : ReceiverIssueReplyDomain));

    internal bool VerifyReceiverIssue(ReadOnlySpan<byte> originalBody, string signature, bool source)
    {
        if (signature.Length != SignatureCharacters
            || signature.Any(character => !char.IsAsciiHexDigitLower(character)))
        { return false; }
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(signature),
            DigestDomain(originalBody, source ? ReceiverIssueSourceDomain : ReceiverIssueReplyDomain));
    }

    internal string SignReceiverIssueRequest(ReadOnlySpan<byte> originalBody, bool query)
        => Convert.ToHexStringLower(DigestDomain(originalBody, query ? ReceiverIssueQueryDomain : ReceiverIssueCommandDomain));

    internal bool VerifyReceiverIssueRequest(ReadOnlySpan<byte> originalBody, string signature, bool query)
    {
        if (signature.Length != SignatureCharacters
            || signature.Any(character => !char.IsAsciiHexDigitLower(character)))
        { return false; }
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(signature),
            DigestDomain(originalBody, query ? ReceiverIssueQueryDomain : ReceiverIssueCommandDomain));
    }

    private byte[] Digest(ReadOnlySpan<byte> originalBody, bool reply)
        => DigestDomain(originalBody, reply ? ReplyDomain : RequestDomain);

    private byte[] DigestDomain(ReadOnlySpan<byte> originalBody, ReadOnlySpan<byte> domain)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != Active, this);
        if (originalBody.IsEmpty || originalBody.Length > maximumBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, InvalidProof); }
        using var hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        hash.AppendData(domain);
        hash.AppendData(originalBody);
        return hash.GetHashAndReset();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, Disposed) == Active)
        { CryptographicOperations.ZeroMemory(key); }
    }
}
