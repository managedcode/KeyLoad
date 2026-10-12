using KeyLoad.Core.Features.Messaging;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsNativeModelObservation(RequestCqrsProbeFiles files,
    RequestCqrsProbeLifecycle lifecycle, IOptions<RequestProbeExecutionOptions> options,
    string voter, string address,
    Func<RequestCqrsProbeClaim, RequestCqrsProbePhase, RequestCqrsProbeOutcome, RequestCqrsProbeMarkerRecord> marker,
    Func<RequestCqrsProbeClaim, RequestCqrsProbePhase, IGrainContext?, CancellationToken, Task> hold)
{
    internal async ValueTask ObserveAsync(RemoteModelNativeRequest original, RemoteModelNativeReply? actual,
        RequestCqrsProbePhase phase, CancellationToken cancellationToken)
    {
        lifecycle.EnterCallback();
        var gate = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var claim = Claim(original, phase);
            if (claim is null) { return; }
            files.RequireActiveArm(claim.Arm);
            lifecycle.EnterGate();
            gate = true;
            var witness = Capture(claim, original, actual, phase);
            files.WriteNativeModel(witness, marker(claim, phase, RequestCqrsProbeOutcome.Observed));
            await hold(claim, phase, null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (gate) { lifecycle.ExitGate(); }
            lifecycle.ExitCallback();
        }
    }

    private const int NoReplyBytes = 0;

    private RequestCqrsProbeClaim? Claim(RemoteModelNativeRequest original, RequestCqrsProbePhase phase)
    {
        if (RequestContext.Get(GrainRequestStreamProtocol.ContextKey) is not GrainRequestContextState context)
        { return null; }
        var claim = lifecycle.FindClaim(context.RequestId);
        if (claim is null || claim.Arm.Record.Phase != phase) { return null; }
        if (context.ConnectionId == Guid.Empty || claim.ConnectionId != context.ConnectionId
            || claim.Identity.RequestId != context.RequestId || claim.Identity.CommandId != context.CommandId
            || claim.Identity.ReadKind is not null || claim.Arm.Record.Action != RequestCqrsProbeAction.Hold
            || original.CallerVoter != voter || original.CallerSiloAddress != address) { throw Invalid(); }
        if (original.Channel != RemoteModelNativeChannel.DocumentEnvelope) { return null; }
        var call = NativeSerialization.Deserialize<RemoteDocumentTransportEnvelope>(original.Payload.Span).QueueTransfer;
        if (call is null || call.Stage != RemoteQueueTransferPeerStage.Accept) { return null; }
        if (call.RequestId != context.RequestId || call.OriginalCommandId != context.CommandId
            || call.LogicalPrincipalId != claim.Identity.PrincipalId) { throw Invalid(); }
        return claim;
    }

    private RequestCqrsNativeModelWitness Capture(RequestCqrsProbeClaim claim,
        RemoteModelNativeRequest original, RemoteModelNativeReply? actual, RequestCqrsProbePhase phase)
    {
        var maximum = options.Value.MaximumRecordBytes;
        var requestBytes = NativeSerialization.Measure(original);
        var replyBytes = actual is null ? NoReplyBytes : NativeSerialization.Measure(actual);
        if (checked(requestBytes + replyBytes) > maximum) { throw Invalid(); }
        return new(RequestCqrsProbeProtocol.Version, files.SessionId, claim.Arm.Record.ArmId,
            claim.Identity.RequestId, claim.Identity.CommandId, claim.ConnectionId, phase, voter, address,
            NativeSerialization.Serialize(original), actual is null ? null : NativeSerialization.Serialize(actual));
    }

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidRecord);
}
