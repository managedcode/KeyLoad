using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace KeyLoad.Core;

internal static class EventVectorFirstOpenOwnedEncoding
{
    internal static EventVectorPreparedFirstOpen Create(EventVectorControlPhase admitted,
        ReadOnlyMemory<byte> originalEntries, EventVectorSourcePhase? first,
        EventVectorAdmissionPolicy admission, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        byte[]? entries = null;
        var checksum = ReadOnlyMemory<byte>.Empty;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            admission.RequireEncodedBytes(originalEntries.Length);
            work.ChargeBytes(originalEntries.Length);
            work.ChargeBytes(SHA256.HashSizeInBytes);
            entries = originalEntries.ToArray();
            checksum = EventVectorEntryEncoding.Digest(entries);
            var phase = admitted with
            {
                Action = EventVectorControlAction.Open,
                OriginalEncodedEntries = entries,
                OriginalEntryChecksum = checksum,
                IntendedSourcePhase = first
            };
            EventVectorControlFields.Require(phase, admission);
            cancellationToken.ThrowIfCancellationRequested();
            work.Check();
            return new(phase);
        }
        catch (Exception)
        {
            if (entries is not null)
            {
                CryptographicOperations.ZeroMemory(entries);
            }
            if (!checksum.IsEmpty)
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(checksum).Span);
            }
            if (first is not null)
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(first.OriginalNativeBody).Span);
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(first.OriginalBodyDigest).Span);
            }
            throw;
        }
    }
}
