using System.Text;
using System.Text.Json;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class NativeSignedPurposeJson
{
    private readonly IOptions<RequestProbeExecutionOptions> options;
    private readonly RequestCqrsProbeJson shape;
    private readonly NativeSignedPurposeJsonContext context;
    private static readonly string[] ArmFields = [nameof(NativeSignedPurposeArm.Version), nameof(NativeSignedPurposeArm.SessionId), nameof(NativeSignedPurposeArm.ArmId), nameof(NativeSignedPurposeArm.SourceVoter), nameof(NativeSignedPurposeArm.Route), nameof(NativeSignedPurposeArm.CommandId)];
    private static readonly string[] WitnessFields = [nameof(NativeSignedPurposeWitness.Version), nameof(NativeSignedPurposeWitness.SessionId), nameof(NativeSignedPurposeWitness.ArmId), nameof(NativeSignedPurposeWitness.SourceVoter), nameof(NativeSignedPurposeWitness.Route),
        nameof(NativeSignedPurposeWitness.ConnectionId), nameof(NativeSignedPurposeWitness.RequestId), nameof(NativeSignedPurposeWitness.CommandId), nameof(NativeSignedPurposeWitness.Incarnation), nameof(NativeSignedPurposeWitness.PrincipalId), nameof(NativeSignedPurposeWitness.ExpiresAt), nameof(NativeSignedPurposeWitness.OriginalPurpose),
        nameof(NativeSignedPurposeWitness.PayloadDigest), nameof(NativeSignedPurposeWitness.Stage), nameof(NativeSignedPurposeWitness.ReceiverVoter)];
    internal NativeSignedPurposeJson(IOptions<RequestProbeExecutionOptions> options)
    {
        this.options = options;
        shape = new(options);
        context = new(new JsonSerializerOptions
        {
            MaxDepth = options.Value.MaximumJsonDepth,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true
        });
    }
    internal NativeSignedPurposeArm Arm(ReadOnlySpan<byte> bytes)
    {
        var value = shape.Read(bytes, ArmFields, context.NativeSignedPurposeArm);
        Require(value);
        return value;
    }
    internal NativeSignedPurposeWitness Witness(ReadOnlySpan<byte> bytes)
    {
        var value = shape.Read(bytes, WitnessFields, context.NativeSignedPurposeWitness);
        Require(new(value.Version, value.SessionId, value.ArmId, value.SourceVoter, value.Route, value.CommandId));
        if (value.ConnectionId == Guid.Empty || value.RequestId == Guid.Empty || value.Incarnation == Guid.Empty
            || string.IsNullOrWhiteSpace(value.PrincipalId)
            || Encoding.UTF8.GetByteCount(value.PrincipalId) > options.Value.MaximumPrincipalBytes
            || value.OriginalPurpose != KeyLoad.Clustering.GrainNativeContracts.RequestPurpose
            || value.PayloadDigest.Length != NativeSignedPurposeProtocol.DigestHexCharacters
            || !value.PayloadDigest.All(Uri.IsHexDigit)
            || value.Stage is not (NativeSignedPurposeProtocol.Issued or NativeSignedPurposeProtocol.Rejected)
            || (value.Stage == NativeSignedPurposeProtocol.Issued ? value.ReceiverVoter is not null
                : string.IsNullOrWhiteSpace(value.ReceiverVoter)
                    || Encoding.UTF8.GetByteCount(value.ReceiverVoter) > options.Value.MaximumPrincipalBytes))
        {
            throw Invalid();
        }
        return value;
    }
    internal byte[] Write(NativeSignedPurposeWitness value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, context.NativeSignedPurposeWitness);
        _ = Witness(bytes);
        return bytes;
    }
    private void Require(NativeSignedPurposeArm value)
    {
        if (value.Version != NativeDiscoveryOmissionProtocol.Version || value.ArmId == Guid.Empty
            || value.CommandId == Guid.Empty || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId)
            || value.Route is not (NativeDiscoveryOmissionProtocol.SdkRoute or NativeDiscoveryOmissionProtocol.McpRoute)
            || string.IsNullOrWhiteSpace(value.SourceVoter)
            || Encoding.UTF8.GetByteCount(value.SourceVoter) > options.Value.MaximumPrincipalBytes)
        {
            throw Invalid();
        }
    }
    private static InvalidOperationException Invalid() => new(NativeSignedPurposeProtocol.Invalid);
}
