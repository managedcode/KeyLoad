using System.Text.Json;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

using static KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsProbeFixtureProtocol;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Emits only the literal, documented private control records consumed by the server codec.</summary>
internal static class RequestCqrsProbeJsonWriter
{
    internal static byte[] Owner(string sessionId, string voter)
        => Encode(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber(VersionField, RequestCqrsProbeFixtureProtocol.Version);
            writer.WriteString(KindField, OwnerKind);
            writer.WriteString(SessionField, sessionId);
            writer.WriteString(VoterField, voter);
            writer.WriteEndObject();
        });

    internal static byte[] Arm(string sessionId, Guid armId, string principalId, Guid commandId,
        GrainReadKind? readKind, RequestCqrsProbePhase phase, RequestCqrsProbeAction action)
        => Encode(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber(VersionField, RequestCqrsProbeFixtureProtocol.Version);
            writer.WriteString(KindField, ArmKind);
            writer.WriteString(SessionField, sessionId);
            writer.WriteString(ArmIdField, armId);
            writer.WriteString(PrincipalField, principalId);
            writer.WriteString(CommandIdField, commandId);
            if (readKind is { } kind)
            { writer.WriteString(ReadKindField, kind.ToString()); }
            else
            { writer.WriteNull(ReadKindField); }
            writer.WriteString(PhaseField, phase.ToString());
            writer.WriteString(ActionField, action.ToString());
            writer.WriteEndObject();
        });

    internal static byte[] Release(string sessionId, Guid armId, Guid requestId)
        => Encode(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber(VersionField, RequestCqrsProbeFixtureProtocol.Version);
            writer.WriteString(KindField, ReleaseKind);
            writer.WriteString(SessionField, sessionId);
            writer.WriteString(ArmIdField, armId);
            writer.WriteString(RequestIdField, requestId);
            writer.WriteEndObject();
        });

    private static byte[] Encode(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream(InitialRecordCapacity);
        using (var writer = new Utf8JsonWriter(stream))
        { write(writer); }
        var bytes = stream.ToArray();
        if (bytes.Length > MaximumRecordBytes)
        { throw new InvalidOperationException(RecordLimitExceeded); }
        return bytes;
    }
}
