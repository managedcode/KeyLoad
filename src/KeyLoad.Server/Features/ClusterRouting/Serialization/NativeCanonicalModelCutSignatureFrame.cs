using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server;

internal static class NativeCanonicalModelCutSignatureFrame
{
    private const int EmptyPayloadBytes = 0;
    private const string Purpose = "keyload.probe.native-canonical-model-cut.v1\0";
    private static readonly byte[] PurposeBytes = System.Text.Encoding.UTF8.GetBytes(Purpose);
    internal static byte[] Create(ReadOnlySpan<byte> payload)
    {
        if (payload.Length is <= EmptyPayloadBytes or > NativeCanonicalModelCutCodec.MaximumRecordBytes)
        { throw NativeCanonicalModelCutCodec.Exhausted(); }
        var purpose = PurposeBytes.AsSpan();
        var frame = new byte[checked(purpose.Length + payload.Length)];
        purpose.CopyTo(frame);
        payload.CopyTo(frame.AsSpan(purpose.Length));
        return frame;
    }
}
