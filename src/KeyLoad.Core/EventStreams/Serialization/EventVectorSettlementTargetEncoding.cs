using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace KeyLoad.Core;

internal static class EventVectorSettlementTargetEncoding
{
    private const string Invalid = "The original event vector settlement target encoding is inconsistent.";

    internal static (ReadOnlyMemory<byte> Bytes, ReadOnlyMemory<byte> Digest) Copy(EventVectorOffer offer,
        EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(offer);
        admission.RequireEncodedBytes(offer.Entries.Length);
        admission.RequireEncodedBytes(offer.PageDigest.Length);
        work.ChargeBytes(offer.Entries.Length);
        work.ChargeBytes(offer.PageDigest.Length);
        var bytes = offer.Entries.ToArray();
        try
        {
            return (bytes, offer.PageDigest.ToArray());
        }
        catch (Exception) { CryptographicOperations.ZeroMemory(bytes); throw; }
    }

    internal static (ReadOnlyMemory<byte> Bytes, ReadOnlyMemory<byte> Digest) Encode(
        ImmutableArray<EventVectorEntry> entries, EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        var length = NativeSerialization.Measure(entries);
        admission.RequireEncodedBytes(length);
        work.ChargeBytes(length);
        work.ChargeBytes(SHA256.HashSizeInBytes);
        var bytes = NativeSerialization.Serialize(entries);
        try
        {
            if (bytes.Length != length)
            {
                throw Errors.Fail(ErrorCode.Corruption, Invalid);
            }
            return (bytes, SHA256.HashData(bytes));
        }
        catch (Exception) { CryptographicOperations.ZeroMemory(bytes); throw; }
    }

    internal static void Clear(ReadOnlyMemory<byte> bytes)
        => CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(bytes).Span);
}
