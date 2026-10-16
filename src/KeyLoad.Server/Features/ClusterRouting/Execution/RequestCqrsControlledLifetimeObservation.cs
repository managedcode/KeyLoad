using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using Microsoft.Extensions.Options;
using static KeyLoad.Server.Features.ClusterRouting.RequestCqrsControlledLifetimeProtocol;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsControlledLifetimeObservation(RequestCqrsProbeFiles files,
    RequestCqrsProbeLifecycle lifecycle, IOptions<RequestProbeExecutionOptions> options, TimeProvider clock,
    string voter, string address,
    Func<RequestCqrsProbeClaim, RequestCqrsProbePhase, RequestCqrsProbeOutcome, RequestCqrsProbeMarkerRecord> marker,
    CancellationToken stopping, CancellationToken applicationStopping) : IControlledLifetimeReplayObserver
{
    public bool Select(PartitionMovementTransportRequest original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var claim = Claim(original);
        if (claim is null)
        {
            return false;
        }
        _ = files.ControlledLifetime.RequirePlan(claim);
        return true;
    }

    public async ValueTask ObserveAsync(PartitionMovementTransportRequest original,
        ControlledLifetimeReplayExchange owner, CancellationToken token)
    {
        lifecycle.EnterCallback();
        var gate = false;
        var baselinePublished = false;
        RequestCqrsProbeClaim? claimed = null;
        try
        {
            using var ceiling = new CancellationTokenSource(options.Value.HoldTimeout, clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, stopping, applicationStopping, ceiling.Token);
            claimed = Claim(original) ?? throw InvalidProof();
            var plan = files.ControlledLifetime.RequirePlan(claimed);
            lifecycle.EnterGate();
            gate = true;
            if (owner.CarrierLength > options.Value.MaximumAggregateBytes)
            {
                throw InvalidProof();
            }
            var carrier = owner.OriginalCarrier;
            var baseline = RequestCqrsControlledLifetimeCapture.Baseline(claimed, plan, original, owner, carrier, options.Value.MaximumRecordBytes);
            files.ControlledLifetime.Publish(baseline, carrier, null, null);
            baselinePublished = true;
            files.WriteMarker(marker(claimed, RequestCqrsProbePhase.ControlledLifetimeReplayBeforeDispatch, RequestCqrsProbeOutcome.Observed));
            await WaitAsync(claimed, BaselineStep, linked.Token).ConfigureAwait(false);
            var donor = plan.SourceArmId is null ? null : files.ControlledLifetime.ReadDonor(claimed);
            for (var step = FirstNegativeStep; step <= RequestCqrsControlledLifetimeInventory.Last(plan); step += IndexStep)
            {
                linked.Token.ThrowIfCancellationRequested();
                files.RequireActiveArm(claimed.Arm);
                _ = files.ControlledLifetime.RequirePlan(claimed);
                var actual = await owner.ExecuteAsync(ControlledLifetimeReplayMutation.At(step), donor, linked.Token).ConfigureAwait(false);
                var captured = RequestCqrsControlledLifetimeCapture.Negative(baseline, step, actual, options.Value.MaximumRecordBytes);
                files.ControlledLifetime.Publish(captured.Witness, null, captured.Request, captured.Reply);
                await WaitAsync(claimed, step, linked.Token).ConfigureAwait(false);
            }
            linked.Token.ThrowIfCancellationRequested();
            files.WriteMarker(marker(claimed, RequestCqrsProbePhase.ControlledLifetimeReplayBeforeDispatch, RequestCqrsProbeOutcome.Released));
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            if (claimed is not null && baselinePublished)
            {
                try
                {
                    files.WriteMarker(marker(claimed, RequestCqrsProbePhase.ControlledLifetimeReplayBeforeDispatch, RequestCqrsProbeOutcome.Cancelled));
                }
                catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
                {
                    throw new AggregateException(failure, cleanup);
                }
            }
            throw;
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

    private async Task WaitAsync(RequestCqrsProbeClaim claim, int step, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (files.ControlledLifetime.RequireAck(claim, step))
            {
                return;
            }
            await Task.Delay(options.Value.PollInterval, clock, token).ConfigureAwait(false);
        }
    }

    private RequestCqrsProbeClaim? Claim(PartitionMovementTransportRequest original)
    {
        if (RequestContext.Get(GrainRequestStreamProtocol.ContextKey) is not GrainRequestContextState context)
        {
            return null;
        }
        var claim = lifecycle.FindClaim(context.RequestId);
        if (claim is null || claim.Arm.Record.Phase != RequestCqrsProbePhase.ControlledLifetimeReplayBeforeDispatch)
        {
            return null;
        }
        if (context.ConnectionId == Guid.Empty || claim.ConnectionId != context.ConnectionId
            || claim.Identity.RequestId != context.RequestId || claim.Identity.CommandId != context.CommandId
            || claim.Identity.ReadKind is not null || claim.Arm.Record.Action != RequestCqrsProbeAction.Hold
            || original.CallerVoter != voter || original.CallerSiloAddress != address
            || original.Action != PartitionMovementTransportAction.Apply || original.Envelope.Stage != PartitionMovePeerStage.ControlApplyCommand
            || claim.Arm.Record.Partition?.ToPartition() != original.Envelope.Partition)
        {
            throw InvalidProof();
        }
        return claim;
    }
    private static InvalidOperationException InvalidProof() => new(Invalid);
}
