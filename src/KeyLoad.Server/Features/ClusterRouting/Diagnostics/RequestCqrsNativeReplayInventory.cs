using System.Text.Json;
using KeyLoad.Server.Features.DocumentStorage;
using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsNativeReplayInventory
{
    internal const string PlanKind = "native-model-replay-plan.v1";
    private const string PlanPrefix = "native-model-replay-plan-";
    private const string WitnessPrefix = "native-model-replay-witness-";
    private const int MaximumBytes = RequestCqrsProbeProtocol.MaximumRecordBytes;
    private const string NameSeparator = "-";
    private const char NameSeparatorCharacter = '-';
    private const int NameComponentCount = 3;
    private const int ArmComponent = 0;
    private const int RequestComponent = 1;
    private const int OrdinalComponent = 2;
    private const int FirstWitnessOrdinal = 1;
    private const int EmptyNativeBytes = 0;
    private const string FirstOrdinalName = "1";
    private const string SecondOrdinalName = "2";
    private const string ThirdOrdinalName = "3";
    private const string FourthOrdinalName = "4";
    private const string FifthOrdinalName = "5";
    private const string SixthOrdinalName = "6";
    internal static int Count(RemoteModelNativeReplayVariation mode) => RemoteModelNativeReplayCatalog.Count(mode);
    internal static string PlanName(Guid armId) => PlanPrefix + armId.ToString(RequestCqrsProbeProtocol.SessionIdFormat) + RequestCqrsProbeProtocol.JsonFileSuffix;
    internal static string WitnessName(Guid armId, Guid requestId, int ordinal)
        => WitnessPrefix + armId.ToString(RequestCqrsProbeProtocol.SessionIdFormat) + NameSeparator + requestId.ToString(RequestCqrsProbeProtocol.SessionIdFormat) + NameSeparator + ordinal + RequestCqrsProbeProtocol.JsonFileSuffix;

    internal static bool IsPlanName(string name)
        => name.StartsWith(PlanPrefix, StringComparison.Ordinal) && name.EndsWith(RequestCqrsProbeProtocol.JsonFileSuffix, StringComparison.Ordinal)
            && CanonicalGuid(name[PlanPrefix.Length..^RequestCqrsProbeProtocol.JsonFileSuffix.Length]);

    internal static bool IsWitnessName(string name)
    {
        if (!name.StartsWith(WitnessPrefix, StringComparison.Ordinal) || !name.EndsWith(RequestCqrsProbeProtocol.JsonFileSuffix, StringComparison.Ordinal))
        { return false; }
        var parts = name[WitnessPrefix.Length..^RequestCqrsProbeProtocol.JsonFileSuffix.Length].Split(NameSeparatorCharacter);
        return parts.Length == NameComponentCount && CanonicalGuid(parts[ArmComponent]) && CanonicalGuid(parts[RequestComponent])
            && parts[OrdinalComponent] is FirstOrdinalName or SecondOrdinalName or ThirdOrdinalName or FourthOrdinalName or FifthOrdinalName or SixthOrdinalName;
    }

    private static bool CanonicalGuid(string value)
        => Guid.TryParseExact(value, RequestCqrsProbeProtocol.SessionIdFormat, out var parsed) && parsed != Guid.Empty && parsed.ToString(RequestCqrsProbeProtocol.SessionIdFormat) == value;

    internal static RequestCqrsNativeReplayPlan ReadPlan(ReadOnlySpan<byte> bytes)
    {
        RequireLength(bytes.Length);
        RequireUniqueProperties(bytes);
        var plan = JsonSerializer.Deserialize(bytes, RequestCqrsNativeReplayJsonContext.Default.RequestCqrsNativeReplayPlan) ?? throw Invalid();
        RequirePlan(plan);
        return plan;
    }

    internal static byte[] WritePlan(RequestCqrsNativeReplayPlan plan)
    {
        RequirePlan(plan);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(plan, RequestCqrsNativeReplayJsonContext.Default.RequestCqrsNativeReplayPlan);
        RequireLength(bytes.Length);
        return bytes;
    }

    private static void RequirePlan(RequestCqrsNativeReplayPlan plan)
    {
        if (plan.Version != RequestCqrsProbeProtocol.Version || plan.Kind != PlanKind
            || !RequestCqrsProbeOptionsReader.IsSessionId(plan.SessionId) || plan.ArmId == Guid.Empty
            || plan.CommandId == Guid.Empty || !Enum.IsDefined(plan.ReplayMode)
            || string.IsNullOrWhiteSpace(plan.PrincipalId) || !plan.PrincipalId.StartsWith(RequestCqrsProbeProtocol.PrincipalPrefix, StringComparison.Ordinal)
            || System.Text.Encoding.UTF8.GetByteCount(plan.PrincipalId) > RequestCqrsProbeProtocol.MaximumPrincipalBytes)
        { throw Invalid(); }
    }

    internal static void RequireArm(RequestCqrsNativeReplayPlan plan, RequestCqrsProbeArmRecord arm)
    {
        RequirePlan(plan);
        if (plan.SessionId != arm.SessionId || plan.ArmId != arm.ArmId || plan.CommandId != arm.CommandId
            || plan.PrincipalId != arm.PrincipalId || arm.Phase != RequestCqrsProbePhase.NativeModelReplayBeforeDispatch
            || arm.Action != RequestCqrsProbeAction.Hold || arm.ReadKind is not null)
        { throw Invalid(); }
    }

    internal static RequestCqrsNativeReplayWitness ReadWitness(ReadOnlySpan<byte> bytes)
    {
        RequireLength(bytes.Length);
        RequireUniqueProperties(bytes);
        var value = JsonSerializer.Deserialize(bytes, RequestCqrsNativeReplayJsonContext.Default.RequestCqrsNativeReplayWitness) ?? throw Invalid();
        RequireWitness(value);
        return value;
    }

    internal static byte[] WriteWitness(RequestCqrsNativeReplayWitness value)
    {
        RequireWitness(value);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, RequestCqrsNativeReplayJsonContext.Default.RequestCqrsNativeReplayWitness);
        RequireLength(bytes.Length);
        return bytes;
    }

    internal static RemoteModelNativeRequest Original(RequestCqrsNativeReplayWitness value)
        => NativeSerialization.Deserialize<RemoteModelNativeRequest>(value.OriginalNativeRequest);
    internal static RemoteModelNativeRequest Request(RequestCqrsNativeReplayWitness value)
        => NativeSerialization.Deserialize<RemoteModelNativeRequest>(value.NativeRequest);
    internal static RemoteModelNativeReply Reply(RequestCqrsNativeReplayWitness value)
        => NativeSerialization.Deserialize<RemoteModelNativeReply>(value.NativeReply);

    private static void RequireWitness(RequestCqrsNativeReplayWitness value)
    {
        if (value.Version != RequestCqrsProbeProtocol.Version || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId)
            || value.ArmId == Guid.Empty || value.RequestId == Guid.Empty || value.CommandId == Guid.Empty
            || value.ConnectionId == Guid.Empty || !RemoteModelNativeReplayCatalog.IsIndividual(value.ReplayMode) || value.Ordinal < FirstWitnessOrdinal
            || value.Ordinal > RemoteModelNativeReplayCatalog.MaximumWitnessOrdinal(value.ReplayMode)
            || value.OriginalNativeRequest is not { Length: > EmptyNativeBytes } || value.NativeRequest is not { Length: > EmptyNativeBytes } || value.NativeReply is not { Length: > EmptyNativeBytes })
        { throw Invalid(); }
        RequireLength(value.OriginalNativeRequest.Length);
        RequireLength(value.NativeRequest.Length);
        RequireLength(value.NativeReply.Length);
        var original = Original(value);
        var request = Request(value);
        var reply = Reply(value);
        if (original is null || request is null || reply is null || original.Nonce == Guid.Empty || original.Nonce == request.Nonce
            || request.Nonce == Guid.Empty || reply.Nonce != request.Nonce
            || request.CallerVoter != value.Voter || request.CallerSiloAddress != value.SiloAddress
            || original.Channel != request.Channel || original.Channel != RemoteModelNativeChannel.DocumentEnvelope
            || original.Signature != request.Signature || original.ExpiresAt != request.ExpiresAt
            || !NativeSerialization.Serialize(original.SourceOwner).AsSpan().SequenceEqual(NativeSerialization.Serialize(request.SourceOwner))
            || !NativeSerialization.Serialize(original.DestinationOwner).AsSpan().SequenceEqual(NativeSerialization.Serialize(request.DestinationOwner)))
        { throw Invalid(); }
        var call = RemoteDocumentWire.DecodeCall(original.Payload.Span).QueueTransfer;
        if (call is null || call.Stage != RemoteQueueTransferPeerStage.Accept || call.RequestId != value.RequestId
            || call.OriginalCommandId != value.CommandId
            || !RemoteModelNativeReplayPayload.Create(original, value.ReplayMode).Span.SequenceEqual(request.Payload.Span))
        { throw Invalid(); }
    }

    internal static void RequireInventory(IReadOnlyList<RequestCqrsNativeReplayPlan> plans,
        IReadOnlyList<RequestCqrsNativeReplayWitness> witnesses, IReadOnlyList<RequestCqrsProbeLoadedArm> arms,
        IReadOnlyList<RequestCqrsProbeMarkerRecord> markers, Func<Guid, RequestCqrsProbeArmRecord?> retained)
    {
        foreach (var plan in plans)
        {
            var active = arms.SingleOrDefault(item => item.Record.ArmId == plan.ArmId);
            var arm = active?.Record ?? retained(plan.ArmId) ?? throw Invalid();
            RequireArm(plan, arm);
        }
        foreach (var witness in witnesses)
        {
            var plan = plans.SingleOrDefault(item => item.ArmId == witness.ArmId) ?? throw Invalid();
            if (plan.SessionId != witness.SessionId || plan.CommandId != witness.CommandId || RemoteModelNativeReplayCatalog.At(plan.ReplayMode, witness.Ordinal) != witness.ReplayMode
                || RemoteDocumentWire.DecodeCall(Original(witness).Payload.Span).QueueTransfer!.LogicalPrincipalId != plan.PrincipalId)
            { throw Invalid(); }
            var marker = markers.SingleOrDefault(item => item.ArmId == witness.ArmId && item.RequestId == witness.RequestId
                && item.Phase == RequestCqrsProbePhase.NativeModelReplayBeforeDispatch && item.Outcome == RequestCqrsProbeOutcome.Observed);
            if (marker.SessionId != witness.SessionId || marker.CommandId != witness.CommandId
                || marker.Voter != witness.Voter || marker.SiloAddress != witness.SiloAddress)
            { throw Invalid(); }
        }
        foreach (var group in witnesses.GroupBy(item => (item.ArmId, item.RequestId)))
        {
            var first = group.First();
            var plan = plans.Single(item => item.ArmId == first.ArmId);
            if (group.Count() != Count(plan.ReplayMode) || group.Select(item => item.Ordinal).Distinct().Count() != group.Count()
                || group.Any(item => item.ConnectionId != first.ConnectionId
                    || !item.OriginalNativeRequest.AsSpan().SequenceEqual(first.OriginalNativeRequest))
                || group.Select(item => Request(item).Nonce).Distinct().Count() != group.Count())
            { throw Invalid(); }
        }
    }

    private static void RequireUniqueProperties(ReadOnlySpan<byte> bytes)
    {
        using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = RequestCqrsProbeProtocol.MaximumJsonDepth });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        { throw Invalid(); }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        { if (!names.Add(property.Name)) { throw Invalid(); } }
    }

    private static void RequireLength(int length) { if (length <= EmptyNativeBytes || length > MaximumBytes) { throw Invalid(); } }
    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidRecord);
}
