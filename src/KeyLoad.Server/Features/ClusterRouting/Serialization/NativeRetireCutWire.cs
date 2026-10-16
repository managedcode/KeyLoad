using System.Security.Cryptography;
using System.Text;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class NativeRetireCutWire
{
    internal const string RequestAlias = "keyload.probe.native-retire-cut-request.v1";
    internal const string WitnessAlias = "keyload.probe.native-retire-cut-witness.v1";
    internal const string Path = "/api/admin/native-retire-cut";
    internal const string SignatureHeader = "X-KeyLoad-Native-Retire-Cut-Signature";
    private const string SignaturePurpose = "keyload.probe.native-retire-cut.v1\0";
    private const string InvalidDetail = "The private Retire cut does not match its original active arm.";
    private const long InitialPosition = 0;

    internal static void Validate(NativeRetireCutRequest request)
    {
        if (request is null || request.Nonce == Guid.Empty || request.ArmId == Guid.Empty
            || request.CommandId == Guid.Empty || !RequestCqrsProbeOptionsReader.IsSessionId(request.SessionId))
        {
            throw Invalid();
        }
        JsonData.Identifier(request.PrincipalId);
        if (request.ReceiptToken is { } token && (token.Incarnation == Guid.Empty
            || token.Position <= InitialPosition || token.OwnershipEpoch <= InitialPosition
            || string.IsNullOrEmpty(token.AtomicPartitionId)))
        {
            throw Invalid();
        }
        if (NativeSerialization.Measure(request) > NativeCanonicalModelCutCodec.MaximumRecordBytes)
        {
            throw NativeCanonicalModelCutCodec.Exhausted();
        }
    }

    internal static byte[] SignatureFrame(ReadOnlySpan<byte> payload)
    {
        var purpose = Encoding.UTF8.GetBytes(SignaturePurpose);
        var digest = SHA256.HashData(payload);
        return [.. purpose, .. digest];
    }

    internal static KeyLoadException Invalid() => Errors.Fail(ErrorCode.Validation, InvalidDetail);
}
