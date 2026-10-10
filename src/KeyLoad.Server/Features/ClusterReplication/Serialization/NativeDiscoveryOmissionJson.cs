using System.Text;
using System.Text.Json;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class NativeDiscoveryOmissionJson
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly string[] ArmFields = [nameof(NativeDiscoveryOmissionArm.Version), nameof(NativeDiscoveryOmissionArm.SessionId),
        nameof(NativeDiscoveryOmissionArm.ArmId), nameof(NativeDiscoveryOmissionArm.SourceVoter), nameof(NativeDiscoveryOmissionArm.TargetVoter), nameof(NativeDiscoveryOmissionArm.Route)];
    private static readonly string[] WitnessFields = [nameof(NativeDiscoveryOmissionWitness.Version), nameof(NativeDiscoveryOmissionWitness.SessionId),
        nameof(NativeDiscoveryOmissionWitness.ArmId), nameof(NativeDiscoveryOmissionWitness.SourceVoter), nameof(NativeDiscoveryOmissionWitness.TargetVoter),
        nameof(NativeDiscoveryOmissionWitness.Route), nameof(NativeDiscoveryOmissionWitness.HttpTraceId), nameof(NativeDiscoveryOmissionWitness.ConnectionId),
        nameof(NativeDiscoveryOmissionWitness.ChildRequestId), nameof(NativeDiscoveryOmissionWitness.CommandId), nameof(NativeDiscoveryOmissionWitness.Nonce),
        nameof(NativeDiscoveryOmissionWitness.Stage), nameof(NativeDiscoveryOmissionWitness.ProducerVoter), nameof(NativeDiscoveryOmissionWitness.RuntimeJournalReaderContract)];
    private readonly IOptions<RequestProbeExecutionOptions> options;
    private readonly RequestCqrsProbeJson shape;
    private readonly NativeDiscoveryOmissionJsonContext context;
    internal NativeDiscoveryOmissionJson(IOptions<RequestProbeExecutionOptions> options)
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
    internal NativeDiscoveryOmissionArm ReadArm(ReadOnlySpan<byte> bytes)
    {
        var value = shape.Read(bytes, ArmFields, context.NativeDiscoveryOmissionArm);
        Require(value);
        return value;
    }
    internal NativeDiscoveryOmissionWitness ReadWitness(ReadOnlySpan<byte> bytes)
    {
        var value = shape.Read(bytes, WitnessFields, context.NativeDiscoveryOmissionWitness);
        Require(new(value.Version, value.SessionId, value.ArmId, value.SourceVoter, value.TargetVoter, value.Route));
        if (value.ConnectionId == Guid.Empty || value.ChildRequestId == Guid.Empty || value.CommandId != Guid.Empty
            || value.Nonce == Guid.Empty || string.IsNullOrWhiteSpace(value.HttpTraceId)
            || Utf8.GetByteCount(value.HttpTraceId) > options.Value.MaximumPrincipalBytes
            || value.Stage is not (NativeDiscoveryOmissionProtocol.SourceStage
                or NativeDiscoveryOmissionProtocol.OmittedStage or NativeDiscoveryOmissionProtocol.VerifiedStage))
        { throw Invalid(); }
        if (value.Stage == NativeDiscoveryOmissionProtocol.SourceStage
            ? value.RuntimeJournalReaderContract is not null || value.ProducerVoter is not null
            : value.RuntimeJournalReaderContract != KeyLoad.Storage.StoreReaderContract.Unspecified || value.ProducerVoter != value.TargetVoter)
        { throw Invalid(); }
        return value;
    }
    internal byte[] Write(NativeDiscoveryOmissionWitness value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, context.NativeDiscoveryOmissionWitness);
        _ = ReadWitness(bytes);
        return bytes;
    }
    private void Require(NativeDiscoveryOmissionArm value)
    {
        if (value.Version != NativeDiscoveryOmissionProtocol.Version || value.ArmId == Guid.Empty
            || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId) || value.SourceVoter == value.TargetVoter
            || value.Route is not (NativeDiscoveryOmissionProtocol.SdkRoute or NativeDiscoveryOmissionProtocol.McpRoute)
            || string.IsNullOrWhiteSpace(value.SourceVoter) || string.IsNullOrWhiteSpace(value.TargetVoter)
            || Utf8.GetByteCount(value.SourceVoter) > options.Value.MaximumPrincipalBytes
            || Utf8.GetByteCount(value.TargetVoter) > options.Value.MaximumPrincipalBytes)
        { throw Invalid(); }
    }
    private static InvalidOperationException Invalid() => new(NativeDiscoveryOmissionProtocol.Invalid);
}
