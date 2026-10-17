using System.Text;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class NativeCommandLifetimeCutWire
{
    internal const string RequestAlias = "keyload.probe.native-command-lifetime-cut-request.v1";
    internal const string WitnessAlias = "keyload.probe.native-command-lifetime-cut-witness.v1";
    internal const string Path = "/api/admin/native-command-lifetime-cut";
    internal const string SignatureHeader = "X-KeyLoad-Native-Command-Lifetime-Cut-Signature";
    internal const int MaximumRecordBytes = NativeCanonicalModelCutCodec.MaximumRecordBytes;
    private const int EmptyBytes = 0;
    private const string Purpose = "keyload.probe.native-command-lifetime-cut.v1\0";
    private static readonly byte[] PurposeBytes = Encoding.UTF8.GetBytes(Purpose);

    internal static byte[] Frame(ReadOnlySpan<byte> payload)
    {
        if (payload.Length is <= EmptyBytes or > MaximumRecordBytes)
        {
            throw NativeCanonicalModelCutCodec.Exhausted();
        }
        var result = new byte[checked(PurposeBytes.Length + payload.Length)];
        PurposeBytes.AsSpan().CopyTo(result);
        payload.CopyTo(result.AsSpan(PurposeBytes.Length));
        return result;
    }
}
