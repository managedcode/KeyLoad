using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.Search;

internal static class NativeTextCanonicalStateCapture
{
    internal static NativeTextCanonicalState Capture(ZoneTreeStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var accumulator = new NativeTextCanonicalStateAccumulator(hash);
        var scan = store.Read(view => view.VisitRange([], NativeTextCrashProtocol.MaximumCanonicalRecords,
            accumulator.Append,
            observer: accumulator.Observe));
        return accumulator.Complete(scan, hash.GetHashAndReset());
    }
}

internal sealed class NativeTextCanonicalStateAccumulator(IncrementalHash hash)
{
    private readonly byte[] _length = new byte[sizeof(long)];
    private int _records;
    private long _examinedBytes;

    internal bool Append(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        if (_records >= NativeTextCrashProtocol.MaximumCanonicalRecords)
        {
            throw new InvalidOperationException("Canonical native-text state exceeds its record bound.");
        }
        AppendField(key);
        AppendField(value);
        _records++;
        return true;
    }

    internal void Observe(long bytes)
    {
        if (bytes < 0 || bytes > NativeTextCrashProtocol.MaximumCanonicalBytes - _examinedBytes)
        {
            throw new InvalidOperationException("Canonical native-text state exceeds its byte bound.");
        }
        _examinedBytes += bytes;
    }

    internal NativeTextCanonicalState Complete(StorageScanResult scan, byte[] digest)
    {
        if (scan.HasMore || scan.Records != _records || scan.ReadBytes != _examinedBytes
            || scan.Records > NativeTextCrashProtocol.MaximumCanonicalRecords
            || scan.ReadBytes > NativeTextCrashProtocol.MaximumCanonicalBytes)
        {
            throw new InvalidOperationException("Canonical native-text state scan exceeded its bound.");
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
