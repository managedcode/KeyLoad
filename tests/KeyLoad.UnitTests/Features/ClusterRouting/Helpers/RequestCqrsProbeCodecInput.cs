using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsProbeCodecInput
{
    internal const string VersionProperty = "Version";
    internal const string UnknownProperty = "Unexpected";
    internal const string VoterProperty = "Voter";
    internal const string ArmIdProperty = "ArmId";
    internal const string ActionProperty = "Action";
    internal const string RequestIdProperty = "RequestId";
    internal const string RequestIdValue = "\"00000000-0000-0000-0000-000000000004\"";
    internal const string SiloAddressProperty = "SiloAddress";
    internal const string SiloAddressValue = "\"127.0.0.1:11111@generation-1\"";
    internal const string VersionToken = "\"Version\":1";
    internal const string ArmIdValue = "\"00000000-0000-0000-0000-000000000002\"";
    internal const string RequestStartedPhaseToken = "\"Phase\":\"RequestStarted\"";
    internal const string LowerVersionToken = "\"version\":1";
    internal const string EscapedVersionToken = "\"\\u0056ersion\":1";
    internal const string OwnerKindToken = "\"Kind\":\"Owner\"";
    internal const string InvalidOwnerKindToken = "\"Kind\":\"Other\"";
    internal const string OwnerVersionToken = "\"Version\":1";
    internal const string InvalidOwnerVersionToken = "\"Version\":2";
    internal const string VoterValue = "\"Voter\":\"http://node-one/\"";
    internal const string NestedVoterValue = "\"Voter\":{\"nested\":true}";
    internal const string NullVoterValue = "\"Voter\":null";
    internal const string HoldActionToken = "\"Action\":\"Hold\"";
    internal const string ObservedOutcomeToken = "\"Outcome\":\"Observed\"";
    internal const string IntegerActionToken = "\"Action\":0";
    internal const string InvalidActionToken = "\"Action\":\"Unknown\"";
    internal const string InvalidPhaseToken = "\"Phase\":\"Unknown\"";
    internal const string InvalidOutcomeToken = "\"Outcome\":\"Unknown\"";
    internal const string TrailingObject = "{}";
    internal const string Space = " ";
    internal const int PrivateRecordVersion = 1;
    internal const string OwnerKind = "Owner";
    internal const string ArmKind = "Arm";
    internal const string ReleaseKind = "Release";
    internal const string MarkerKind = "Marker";
    internal const string TestPrincipal = "c1-probe-unit-owner";
    internal static string MaximumPrincipal => RequestCqrsProbeProtocol.PrincipalPrefix
        + new string('a', RequestCqrsProbeProtocol.MaximumPrincipalBytes - Encoding.UTF8.GetByteCount(RequestCqrsProbeProtocol.PrincipalPrefix));
    internal static string OverlongPrincipal => MaximumPrincipal + "a";
    internal const string SessionId = "00000000000000000000000000000001";
    internal static readonly Guid ArmId = Guid.ParseExact("00000000000000000000000000000002", "N");
    internal static readonly Guid CommandId = Guid.ParseExact("00000000000000000000000000000003", "N");
    internal static readonly Guid RequestId = Guid.ParseExact("00000000000000000000000000000004", "N");
    internal static readonly RequestCqrsProbePhase[] ActivePhases =
    [
        RequestCqrsProbePhase.RequestStarted, RequestCqrsProbePhase.AuthorizationReload,
        RequestCqrsProbePhase.BeforeSubmit, RequestCqrsProbePhase.SubmitReturned
    ];
    internal static readonly RequestCqrsProbeAction[] Actions = [RequestCqrsProbeAction.Hold, RequestCqrsProbeAction.ThrowOrdinary];
    internal static readonly RequestCqrsProbePhase[] MarkerPhases =
    [
        RequestCqrsProbePhase.RequestStarted, RequestCqrsProbePhase.AuthorizationReload,
        RequestCqrsProbePhase.BeforeSubmit, RequestCqrsProbePhase.SubmitReturned,
        RequestCqrsProbePhase.ProducerDisposed
    ];
    internal static readonly RequestCqrsProbeOutcome[] Outcomes =
    [
        RequestCqrsProbeOutcome.Observed, RequestCqrsProbeOutcome.Released,
        RequestCqrsProbeOutcome.Cancelled, RequestCqrsProbeOutcome.FaultRequested
    ];

    private static readonly RequestCqrsProbeJsonContext Context = CreateContext();

    internal static byte[] Owner(string sessionId = SessionId, string voter = "http://node-one/")
        => JsonSerializer.SerializeToUtf8Bytes(new RequestCqrsProbeOwnerRecord(PrivateRecordVersion, OwnerKind,
            sessionId, voter), Context.RequestCqrsProbeOwnerRecord);

    internal static byte[] Arm(RequestCqrsProbePhase phase = RequestCqrsProbePhase.RequestStarted,
        RequestCqrsProbeAction action = RequestCqrsProbeAction.Hold, Guid? commandId = null,
        GrainReadKind? readKind = null, Guid? armId = null, string principalId = TestPrincipal)
        => JsonSerializer.SerializeToUtf8Bytes(new RequestCqrsProbeArmRecord(PrivateRecordVersion, ArmKind, SessionId,
            armId ?? ArmId, principalId, commandId ?? (readKind.HasValue ? Guid.Empty : CommandId),
            readKind, phase, action), Context.RequestCqrsProbeArmRecord);

    internal static byte[] Release(Guid? requestId = null, Guid? armId = null)
        => JsonSerializer.SerializeToUtf8Bytes(new RequestCqrsProbeReleaseRecord(PrivateRecordVersion, ReleaseKind, SessionId,
            armId ?? ArmId, requestId ?? RequestId), Context.RequestCqrsProbeReleaseRecord);

    internal static byte[] Marker(Guid? armId = null, Guid? requestId = null, Guid? commandId = null,
        RequestCqrsProbePhase phase = RequestCqrsProbePhase.ProducerDisposed,
        RequestCqrsProbeOutcome outcome = RequestCqrsProbeOutcome.Observed)
        => JsonSerializer.SerializeToUtf8Bytes(new RequestCqrsProbeMarkerRecord(PrivateRecordVersion, MarkerKind, SessionId,
            armId ?? ArmId, requestId ?? RequestId, commandId ?? CommandId, phase, outcome,
            "http://node-one/", "127.0.0.1:11111@generation-1"),
            Context.RequestCqrsProbeMarkerRecord);

    internal static byte[] InsertFirstProperty(byte[] bytes, string name, string value)
    {
        var suffix = Encoding.UTF8.GetString(bytes.AsSpan(1));
        return Encoding.UTF8.GetBytes($"{{\"{name}\":{value},{suffix}");
    }

    internal static byte[] Replace(byte[] bytes, string from, string to)
    {
        var text = Encoding.UTF8.GetString(bytes);
        if (!text.Contains(from, StringComparison.Ordinal))
        { throw new InvalidOperationException("Test input token was not present."); }
        return Encoding.UTF8.GetBytes(text.Replace(from, to, StringComparison.Ordinal));
    }

    internal static byte[] RemoveProperty(byte[] bytes, string name)
    {
        var text = Encoding.UTF8.GetString(bytes);
        var property = $"\"{name}\":";
        var start = text.IndexOf(property, StringComparison.Ordinal);
        if (start < 0)
        { throw new InvalidOperationException("Test input property was not present."); }
        var objectEnd = text.IndexOf('}', start + property.Length);
        if (objectEnd < 0)
        { throw new InvalidOperationException("Test input object was not closed."); }
        var comma = text.IndexOf(',', start + property.Length);
        if (comma >= 0 && comma < objectEnd)
        { return Encoding.UTF8.GetBytes(text.Remove(start, comma - start + 1)); }
        var removeStart = start > 0 && text[start - 1] == ',' ? start - 1 : start;
        return Encoding.UTF8.GetBytes(text.Remove(removeStart, objectEnd - removeStart));
    }

    internal static byte[] Append(byte[] bytes, string suffix)
        => [.. bytes, .. Encoding.UTF8.GetBytes(suffix)];

    internal static byte[] PadOwner(int length)
    {
        var bytes = Owner();
        return Append(bytes, new string(' ', length - bytes.Length));
    }

    private static RequestCqrsProbeJsonContext CreateContext()
    {
        var options = new JsonSerializerOptions
        {
            MaxDepth = RequestCqrsProbeProtocol.MaximumJsonDepth,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true
        };
        options.Converters.Add(new JsonStringEnumConverter<RequestCqrsProbePhase>(null, false));
        options.Converters.Add(new JsonStringEnumConverter<RequestCqrsProbeAction>(null, false));
        options.Converters.Add(new JsonStringEnumConverter<RequestCqrsProbeOutcome>(null, false));
        options.Converters.Add(new JsonStringEnumConverter<GrainReadKind>(null, false));
        return new RequestCqrsProbeJsonContext(options);
    }
}
