using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsProbeJson
{
    internal const string InvalidRecord = "The private request probe record is invalid.";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly IOptions<RequestProbeExecutionOptions> executionOptions;
    private readonly RequestCqrsProbeJsonContext Context;

    internal RequestCqrsProbeJson(IOptions<RequestProbeExecutionOptions> executionOptions)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        executionOptions.Value.Validate();
        this.executionOptions = executionOptions;
        Context = CreateContext();
    }

    internal RequestCqrsProbeOwnerRecord ReadOwner(ReadOnlySpan<byte> bytes)
    {
        var value = Read(bytes, RequestCqrsProbeRecordFields.Owner, Context.RequestCqrsProbeOwnerRecord);
        if (value.Version != RequestCqrsProbeProtocol.Version || value.Kind != RequestCqrsProbeProtocol.OwnerKind
            || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId) || string.IsNullOrWhiteSpace(value.Voter))
        { throw Invalid(); }
        return value;
    }

    internal RequestCqrsProbeArmRecord ReadArm(ReadOnlySpan<byte> bytes)
    {
        var value = Read(bytes, RequestCqrsProbeRecordFields.Arm, Context.RequestCqrsProbeArmRecord);
        RequestCqrsProbeArmRecordValidation.Require(value, executionOptions, StrictUtf8);
        return value;
    }

    internal RequestCqrsProbeReleaseRecord ReadRelease(ReadOnlySpan<byte> bytes)
    {
        var value = Read(bytes, RequestCqrsProbeRecordFields.Release, Context.RequestCqrsProbeReleaseRecord);
        if (value.Version != RequestCqrsProbeProtocol.Version || value.Kind != RequestCqrsProbeProtocol.ReleaseKind
            || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId) || value.ArmId == Guid.Empty || value.RequestId == Guid.Empty)
        { throw Invalid(); }
        return value;
    }

    internal RequestCqrsProbeMarkerRecord ReadMarker(ReadOnlySpan<byte> bytes)
    {
        var value = Read(bytes, RequestCqrsProbeRecordFields.Marker, Context.RequestCqrsProbeMarkerRecord);
        if (value.Version != RequestCqrsProbeProtocol.Version || value.Kind != RequestCqrsProbeProtocol.MarkerKind
            || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId) || value.ArmId == Guid.Empty || value.RequestId == Guid.Empty
            || !Enum.IsDefined(value.Phase) || !Enum.IsDefined(value.Outcome)
            || string.IsNullOrWhiteSpace(value.Voter) || string.IsNullOrWhiteSpace(value.SiloAddress)
            || !RequestCqrsCanonicalMarkerValidation.Valid(value))
        { throw Invalid(); }
        return value;
    }

    internal byte[] WriteMarker(RequestCqrsProbeMarkerRecord value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Context.RequestCqrsProbeMarkerRecord);
        if (bytes.Length > executionOptions.Value.MaximumRecordBytes)
        { throw Invalid(); }
        return bytes;
    }

    internal RequestCqrsProbeActivationRecord ReadActivation(ReadOnlySpan<byte> bytes)
    {
        var value = Read(bytes, RequestCqrsProbeRecordFields.Activation, Context.RequestCqrsProbeActivationRecord);
        RequestCqrsProbeActivationValidation.Require(value);
        return value;
    }

    internal byte[] WriteActivation(RequestCqrsProbeActivationRecord value)
    {
        RequestCqrsProbeActivationValidation.Require(value);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Context.RequestCqrsProbeActivationRecord);
        if (bytes.Length > executionOptions.Value.MaximumRecordBytes)
        { throw Invalid(); }
        return bytes;
    }

    internal RequestCqrsProbeLiveRecord ReadLive(ReadOnlySpan<byte> bytes)
    {
        var value = Read(bytes, RequestCqrsProbeRecordFields.Live, Context.RequestCqrsProbeLiveRecord);
        RequestCqrsProbeLiveValidation.Require(value);
        return value;
    }
    internal byte[] WriteLive(RequestCqrsProbeLiveRecord value)
    {
        RequestCqrsProbeLiveValidation.Require(value);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Context.RequestCqrsProbeLiveRecord);
        if (bytes.Length > executionOptions.Value.MaximumRecordBytes)
        { throw Invalid(); }
        return bytes;
    }

    private RequestCqrsProbeJsonContext CreateContext()
    {
        var options = new JsonSerializerOptions
        {
            MaxDepth = executionOptions.Value.MaximumJsonDepth,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true
        };
        options.Converters.Add(new JsonStringEnumConverter<RequestCqrsProbePhase>(null, false));
        options.Converters.Add(new JsonStringEnumConverter<RequestCqrsProbeAction>(null, false));
        options.Converters.Add(new JsonStringEnumConverter<RequestCqrsProbeOutcome>(null, false));
        options.Converters.Add(new JsonStringEnumConverter<KeyLoad.Orleans.GrainReadKind>(null, false));
        return new RequestCqrsProbeJsonContext(options);
    }

    internal System.Text.Json.Serialization.Metadata.JsonTypeInfo<RequestCqrsEventVectorExpiryRecord> EventVectorExpiryTypeInfo
        => Context.RequestCqrsEventVectorExpiryRecord;

    internal System.Text.Json.Serialization.Metadata.JsonTypeInfo<RequestCqrsReplicaSnapshotRecord> ReplicaSnapshotTypeInfo
        => Context.RequestCqrsReplicaSnapshotRecord;

    internal T Read<T>(ReadOnlySpan<byte> bytes, ReadOnlySpan<string> fields, JsonTypeInfo<T> typeInfo) where T : struct
    {
        try
        {
            RequestCqrsProbeJsonShape.Require(bytes, fields, executionOptions, StrictUtf8);
            return JsonSerializer.Deserialize(bytes, typeInfo);
        }
        catch (JsonException)
        { throw Invalid(); }
        catch (EncoderFallbackException)
        { throw Invalid(); }
        catch (DecoderFallbackException)
        { throw Invalid(); }
    }

    internal JsonTypeInfo<RequestCqrsWaitForIndexReadCutWitness> WaitCutTypeInfo
        => Context.RequestCqrsWaitForIndexReadCutWitness;

    internal JsonTypeInfo<RequestCqrsQueryReadCutWitness> QueryCutTypeInfo
        => Context.RequestCqrsQueryReadCutWitness;

    private static InvalidOperationException Invalid() => new(InvalidRecord);
}
