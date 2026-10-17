using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.CrashHost.Features.Search;

internal sealed class NativeTextCanonicalStateAccumulator(IncrementalHash hash)
{
    private readonly byte[] _length = new byte[sizeof(long)];
    private int _records;
    private long _examinedBytes;

    internal bool Append(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        const string AppendMessageText = "Canonical native-text state exceeds its record bound.";

        if (_records >= NativeTextCrashProtocol.MaximumCanonicalRecords)
        {
            throw new InvalidOperationException(AppendMessageText);
        }
        AppendField(key);
        AppendField(value);
        _records++;
        return true;
    }

    internal void Observe(long bytes)
    {
        const int BytesValidationBoundary = 0;
        const string ObserveMessageText = "Canonical native-text state exceeds its byte bound.";

        if (bytes < BytesValidationBoundary || bytes > NativeTextCrashProtocol.MaximumCanonicalBytes - _examinedBytes)
        {
            throw new InvalidOperationException(ObserveMessageText);
        }
        _examinedBytes += bytes;
    }

    internal NativeTextCanonicalState Complete(StorageScanResult scan, byte[] digest)
    {
        const string CompleteMessageText = "Canonical native-text state scan exceeded its bound.";

        if (scan.HasMore || scan.Records != _records || scan.ReadBytes != _examinedBytes
            || scan.Records > NativeTextCrashProtocol.MaximumCanonicalRecords
            || scan.ReadBytes > NativeTextCrashProtocol.MaximumCanonicalBytes)
        {
            throw new InvalidOperationException(CompleteMessageText);
        }
        return new(_records, digest);
    }

    private void AppendField(ReadOnlySpan<byte> bytes)
    {
        BinaryPrimitives.WriteInt64LittleEndian(_length, bytes.Length);
        hash.AppendData(_length);
        hash.AppendData(bytes);
    }
}
