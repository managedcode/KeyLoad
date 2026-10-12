using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.BlobStorage;

internal sealed class NativeBlobWireEvidence(NativeBlobWireFiles files, NativeBlobWireCodec codec,
    IOptions<RequestProbeExecutionOptions> limits)
{
    private const int FirstOrdinal = 1;
    private const int FirstIndex = 0;
    private const int EmptyPayloadBytes = 0;

    internal NativeBlobWireResultEntry Write(NativeBlobWireClaim claim, string claimDigest,
        byte[] original, NativeControlledBlobFaultResult actual, int ordinal, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (actual.Ordinal != ordinal || (int)actual.Variation != ordinal - FirstOrdinal) { throw Invalid(); }
        var record = new NativeBlobWireResult(NativeBlobWireProbeProtocol.Version, claim.SessionId,
            claimDigest, claim.Identity, ordinal, actual.Variation, original,
            EncodeNative(actual.Request), EncodeNative(actual.Reply));
        var bytes = codec.Encode(record);
        return files.WithPublication(() =>
        {
            token.ThrowIfCancellationRequested();
            files.WriteLocked(NativeBlobWireNames.Result(claim.Identity, ordinal), bytes);
            return new NativeBlobWireResultEntry(ordinal, actual.Variation, NativeBlobWireCodec.Digest(bytes));
        });
    }

    internal void Complete(NativeBlobWireClaim claim, string claimDigest,
        NativeBlobWireResultEntry[] results, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var expected = (int)claim.Identity < (int)NativeBlobWireIdentity.Metadata
            ? NativeBlobWireProbeProtocol.MaximumFaultCount : NativeBlobWireProbeProtocol.CommonFaultCount;
        if (results.Length != expected) { throw Invalid(); }
        var record = new NativeBlobWireCompletion(NativeBlobWireProbeProtocol.Version,
            claim.SessionId, claimDigest, claim.Identity, results.Length, results);
        var bytes = codec.Encode(record);
        files.WithPublication(() =>
        {
        token.ThrowIfCancellationRequested();
        for (var index = FirstIndex; index < results.Length; index++)
        {
            var entry = results[index];
            if (entry.Ordinal != index + FirstOrdinal || (int)entry.Variation != index
                || NativeBlobWireCodec.Digest(files.Read(NativeBlobWireNames.Result(claim.Identity, entry.Ordinal))) != entry.RecordDigest)
            { throw Invalid(); }
        }
        files.WriteLocked(NativeBlobWireNames.Completion(claim.Identity), bytes);
        });
    }

    internal byte[] EncodeNative<T>(T value)
    {
        var bytes = NativeSerialization.Measure(value);
        if (bytes <= EmptyPayloadBytes || bytes > limits.Value.MaximumRecordBytes) { throw Invalid(); }
        return NativeSerialization.Serialize(value);
    }
    private static InvalidOperationException Invalid() => new(NativeBlobWireProbeProtocol.InvalidEvidence);
}
