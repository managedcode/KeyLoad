using KeyLoad.Clustering;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsNativeReplayObservation(RequestCqrsProbeFiles files,
    RequestCqrsProbeLifecycle lifecycle, IOptions<RequestProbeExecutionOptions> options, string voter, string address,
    Func<RequestCqrsProbeClaim, RequestCqrsProbePhase, RequestCqrsProbeOutcome, RequestCqrsProbeMarkerRecord> marker,
    Func<RequestCqrsProbeClaim, RequestCqrsProbePhase, IGrainContext?, CancellationToken, Task> hold)
{
    private const int FirstWitnessOrdinal = 1;

    internal RemoteModelNativeReplayVariation? Select(RemoteModelNativeRequest original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var claim = Claim(original);
        return claim is null ? null : files.Replay.RequirePlan(claim).ReplayMode;
    }

    internal async ValueTask ObserveAsync(RemoteModelNativeRequest original, RemoteModelNativeReplayExchange owner,
        CancellationToken token)
    {
        lifecycle.EnterCallback();
        var gate = false;
        try
        {
            token.ThrowIfCancellationRequested();
            var claim = Claim(original) ?? throw Invalid();
            var plan = files.Replay.RequirePlan(claim);
            lifecycle.EnterGate();
            gate = true;
            if (NativeSerialization.Measure(original) > options.Value.MaximumRecordBytes)
            {
                throw Invalid();
            }
            var originalBytes = NativeSerialization.Serialize(original);
            var witnesses = new List<RequestCqrsNativeReplayWitness>();
            for (var ordinal = FirstWitnessOrdinal; ordinal <= RequestCqrsNativeReplayInventory.Count(plan.ReplayMode); ordinal++)
            {
                token.ThrowIfCancellationRequested();
                files.RequireActiveArm(claim.Arm);
                _ = files.Replay.RequirePlan(claim);
                var actual = await owner.ExecuteAsync(RemoteModelNativeReplayCatalog.At(plan.ReplayMode, ordinal), token).ConfigureAwait(false);
                witnesses.Add(Capture(originalBytes, claim, plan, ordinal, actual));
            }
            files.Replay.Publish(claim, witnesses, marker(claim, RequestCqrsProbePhase.NativeModelReplayBeforeDispatch, RequestCqrsProbeOutcome.Observed));
            await hold(claim, RequestCqrsProbePhase.NativeModelReplayBeforeDispatch, null, token).ConfigureAwait(false);
        }
        finally
        {
            if (gate)
            {
                lifecycle.ExitGate();
            }
            lifecycle.ExitCallback();
        }
    }

    private RequestCqrsProbeClaim? Claim(RemoteModelNativeRequest original)
    {
        if (RequestContext.Get(GrainRequestStreamProtocol.ContextKey) is not GrainRequestContextState context)
        {
            return null;
        }
        var claim = lifecycle.FindClaim(context.RequestId);
        if (claim is null || claim.Arm.Record.Phase != RequestCqrsProbePhase.NativeModelReplayBeforeDispatch)
        {
            return null;
        }
        if (context.ConnectionId == Guid.Empty || claim.ConnectionId != context.ConnectionId
            || claim.Identity.RequestId != context.RequestId || claim.Identity.CommandId != context.CommandId
            || claim.Identity.ReadKind is not null || claim.Arm.Record.Action != RequestCqrsProbeAction.Hold
            || original.CallerVoter != voter || original.CallerSiloAddress != address
            || original.Channel != RemoteModelNativeChannel.DocumentEnvelope)
        {
            throw Invalid();
        }
        var call = RemoteDocumentWire.DecodeCall(original.Payload.Span).QueueTransfer;
        if (call is null || call.Stage != RemoteQueueTransferPeerStage.Accept || call.RequestId != context.RequestId
            || call.OriginalCommandId != context.CommandId || call.LogicalPrincipalId != claim.Identity.PrincipalId)
        {
            throw Invalid();
        }
        return claim;
    }

    private RequestCqrsNativeReplayWitness Capture(byte[] originalBytes, RequestCqrsProbeClaim claim, RequestCqrsNativeReplayPlan plan,
        int ordinal, RemoteModelNativeReplayResult actual)
    {
        if (checked(originalBytes.Length + NativeSerialization.Measure(actual.Request) + NativeSerialization.Measure(actual.Reply)) > options.Value.MaximumRecordBytes)
        {
            throw Invalid();
        }
        return new(RequestCqrsProbeProtocol.Version, files.SessionId, claim.Arm.Record.ArmId, claim.Identity.RequestId,
            claim.Identity.CommandId, claim.ConnectionId, actual.Request.CallerVoter, actual.Request.CallerSiloAddress,
            RemoteModelNativeReplayCatalog.At(plan.ReplayMode, ordinal), ordinal, originalBytes, NativeSerialization.Serialize(actual.Request), NativeSerialization.Serialize(actual.Reply));
    }

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidRecord);
}
