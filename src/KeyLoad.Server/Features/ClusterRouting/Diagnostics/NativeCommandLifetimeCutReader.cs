using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class NativeCommandLifetimeCutReader(IOptions<DatabaseLimits> limits, CancellationToken token) : IDisposable
{
    private const int LengthBytes = sizeof(int);
    private const int FrameBytes = LengthBytes + LengthBytes;
    private const int FirstFrameOffset = 0;
    private const int RecordStep = 1;
    private const long InitialCount = 0;
    private const string DigestPurpose = "keyload.probe.native-command-lifetime-models.v1\0";
    private readonly IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private long examined;
    private long readBytes;
    private long modelRows;
    private long modelBytes;

    internal (long Rows, long Bytes, string Digest) ReadModels(IKeyValueView view, PartitionRef partition)
    {
        digest.AppendData(Encoding.UTF8.GetBytes(DigestPurpose));
        foreach (var family in PartitionRecordFamilies.All)
        {
            token.ThrowIfCancellationRequested();
            var rows = view.VisitRange(KeySpace.Partition(family, partition), limits.Value.MaxScanRecords,
                Copy, observer: Charge, cancellationToken: token);
            if (rows.HasMore || rows.StoppedByVisitor)
            {
                throw NativeCanonicalModelCutCodec.Exhausted();
            }
        }
        return (modelRows, modelBytes, Convert.ToHexStringLower(digest.GetHashAndReset()));
    }

    internal ReadOnlyMemory<byte>? Outcome(IKeyValueView view, string principal, Guid command, PartitionRef partition)
    {
        var scope = new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, partition);
        var key = CommandOutcomeKeyResolver.ForNew(principal, command, scope).Key;
        ReadOnlyMemory<byte>? result = null;
        view.ReadValue(key, value =>
        {
            if (value.Length > NativeCommandLifetimeCutWire.MaximumRecordBytes)
            {
                throw NativeCanonicalModelCutCodec.Exhausted();
            }
            result = value.ToArray();
        }, Charge);
        var selected = CommandOutcomeKeyResolver.Select(view, principal, command, scope, Charge);
        if ((selected.Outcome is null) != (result is null))
        {
            throw NativeCanonicalModelCutCodec.Invalid();
        }
        return result;
    }

    private bool Copy(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        token.ThrowIfCancellationRequested();
        Span<byte> frame = stackalloc byte[FrameBytes];
        BinaryPrimitives.WriteInt32LittleEndian(frame[FirstFrameOffset..LengthBytes], key.Length);
        BinaryPrimitives.WriteInt32LittleEndian(frame[LengthBytes..], value.Length);
        digest.AppendData(frame);
        digest.AppendData(key);
        digest.AppendData(value);
        modelRows = checked(modelRows + RecordStep);
        modelBytes = checked(modelBytes + key.Length + value.Length);
        return true;
    }

    private void Charge(long bytes)
    {
        token.ThrowIfCancellationRequested();
        examined = checked(examined + RecordStep);
        readBytes = checked(readBytes + bytes);
        if (bytes < InitialCount || examined > limits.Value.MaxScanRecords || readBytes > limits.Value.MaxQueryReadBytes)
        {
            throw NativeCanonicalModelCutCodec.Exhausted();
        }
    }

    public void Dispose() => digest.Dispose();
}
