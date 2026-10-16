using System.Text.Json;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsNativeModelInventory
{
    private const string Prefix = "native-model-";
    private const string Separator = "-";
    private const int MaximumBytes = RequestCqrsProbeProtocol.MaximumRecordBytes;
    private const int NameComponentCount = 3;
    private const int ArmComponent = 0;
    private const int RequestComponent = 1;
    private const int PhaseComponent = 2;
    private const int EmptyNativeBytes = 0;

    internal static string Name(Guid armId, Guid requestId, RequestCqrsProbePhase phase)
        => string.Concat(Prefix, armId.ToString(RequestCqrsProbeProtocol.SessionIdFormat), Separator,
            requestId.ToString(RequestCqrsProbeProtocol.SessionIdFormat), Separator, phase.ToString(),
            RequestCqrsProbeProtocol.JsonFileSuffix);

    internal static bool IsName(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal)
            || !name.EndsWith(RequestCqrsProbeProtocol.JsonFileSuffix, StringComparison.Ordinal))
        {
            return false;
        }
        var parts = name[Prefix.Length..^RequestCqrsProbeProtocol.JsonFileSuffix.Length].Split(Separator, StringSplitOptions.None);
        return parts.Length == NameComponentCount && CanonicalGuid(parts[ArmComponent]) && CanonicalGuid(parts[RequestComponent])
            && parts[PhaseComponent] is nameof(RequestCqrsProbePhase.NativeModelBeforeDispatch)
                or nameof(RequestCqrsProbePhase.NativeModelAfterDispatch);
    }

    private static bool CanonicalGuid(string text)
        => Guid.TryParseExact(text, RequestCqrsProbeProtocol.SessionIdFormat, out var value)
            && value != Guid.Empty && value.ToString(RequestCqrsProbeProtocol.SessionIdFormat) == text;

    internal static RequestCqrsNativeModelWitness Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length > MaximumBytes)
        {
            throw Invalid();
        }
        var value = JsonSerializer.Deserialize(bytes, RequestCqrsNativeModelJsonContext.Default.RequestCqrsNativeModelWitness)
            ?? throw Invalid();
        Require(value);
        return value;
    }

    internal static byte[] Write(RequestCqrsNativeModelWitness value)
    {
        Require(value);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, RequestCqrsNativeModelJsonContext.Default.RequestCqrsNativeModelWitness);
        if (bytes.Length > MaximumBytes)
        {
            throw Invalid();
        }
        return bytes;
    }

    internal static RemoteModelNativeRequest Request(RequestCqrsNativeModelWitness value)
        => NativeSerialization.Deserialize<RemoteModelNativeRequest>(value.NativeRequest);

    internal static RemoteModelNativeReply Reply(RequestCqrsNativeModelWitness value)
        => NativeSerialization.Deserialize<RemoteModelNativeReply>(value.NativeReply ?? throw Invalid());

    internal static void RequireMarkers(IEnumerable<RequestCqrsNativeModelWitness> values,
        IReadOnlyList<RequestCqrsProbeMarkerRecord> markers)
    {
        foreach (var value in values)
        {
            var marker = markers.SingleOrDefault(item => item.ArmId == value.ArmId && item.RequestId == value.RequestId
                && item.Phase == value.Phase && item.Outcome == RequestCqrsProbeOutcome.Observed);
            if (marker.SessionId != value.SessionId || marker.CommandId != value.CommandId
                || marker.Voter != value.Voter || marker.SiloAddress != value.SiloAddress)
            {
                throw Invalid();
            }
        }
    }

    private static void Require(RequestCqrsNativeModelWitness value)
    {
        if (value.Version != RequestCqrsProbeProtocol.Version || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId)
            || value.ArmId == Guid.Empty || value.RequestId == Guid.Empty
            || value.ConnectionId == Guid.Empty || string.IsNullOrWhiteSpace(value.Voter) || string.IsNullOrWhiteSpace(value.SiloAddress)
            || value.Phase is not (RequestCqrsProbePhase.NativeModelBeforeDispatch or RequestCqrsProbePhase.NativeModelAfterDispatch)
            || value.NativeRequest is not { Length: > EmptyNativeBytes } || value.NativeRequest.Length > MaximumBytes
            || (value.Phase == RequestCqrsProbePhase.NativeModelBeforeDispatch ? value.NativeReply is not null
                : value.NativeReply is not { Length: > EmptyNativeBytes })
            || value.NativeReply is { } bytes && bytes.Length > MaximumBytes)
        {
            throw Invalid();
        }
        var request = Request(value);
        if (request is null || request.CallerVoter != value.Voter || request.CallerSiloAddress != value.SiloAddress
            || request.Nonce == Guid.Empty || string.IsNullOrWhiteSpace(request.SenderSignature))
        {
            throw Invalid();
        }
        if (value.CommandId == Guid.Empty && (value.Phase != RequestCqrsProbePhase.NativeModelAfterDispatch
            || request.Channel is not (RemoteModelNativeChannel.ResourceLifetimeRead or RemoteModelNativeChannel.ResourceLifetimeAnnRead)))
        {
            throw Invalid();
        }
        if (value.CommandId == Guid.Empty)
        {
            RequestCqrsLifetimeReadProbe.RequireReadWitness(request, value.RequestId);
        }
        if (value.NativeReply is not null)
        {
            var reply = Reply(value);
            if (reply is null || reply.Nonce != request.Nonce)
            {
                throw Invalid();
            }
        }
    }

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidRecord);
}
