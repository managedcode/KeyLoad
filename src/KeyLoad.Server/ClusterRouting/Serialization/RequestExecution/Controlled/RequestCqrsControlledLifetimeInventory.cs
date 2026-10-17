using Microsoft.Extensions.Options;
using static KeyLoad.Server.Features.ClusterRouting.RequestCqrsControlledLifetimeProtocol;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsControlledLifetimeInventory(string sessionId,
    IOptions<RequestProbeExecutionOptions> options, RequestCqrsProbeRecords records)
{
    private readonly List<RequestCqrsControlledLifetimePlan> plans = [];
    private readonly List<RequestCqrsControlledLifetimeStepWitness> steps = [];
    private readonly List<RequestCqrsControlledLifetimeStepAck> acks = [];
    private readonly Dictionary<string, byte[]> parts = new(StringComparer.Ordinal);

    internal void Read(string path, string name, HashSet<string> present)
    {
        if (!RequestCqrsControlledLifetimeNames.IsName(name))
        {
            throw InvalidProof();
        }
        var bytes = RequestCqrsProbeFiles.ReadRecord(path, options);
        ReadEntry(name, bytes);
        records.RegisterImmutable(name, bytes);
        present.Add(name);
    }

    private void ReadEntry(string name, byte[] bytes)
    {
        if (name.StartsWith(PlanPrefix, StringComparison.Ordinal))
        {
            var value = RequestCqrsControlledLifetimeCodec.ReadPlan(bytes);
            if (value.SessionId != sessionId || name != RequestCqrsControlledLifetimeNames.PlanName(value.ArmId))
            {
                throw InvalidProof();
            }
            plans.Add(value);
            return;
        }
        if (name.StartsWith(StepPrefix, StringComparison.Ordinal))
        {
            var value = RequestCqrsControlledLifetimeCodec.ReadWitness(bytes);
            if (value.SessionId != sessionId || name != RequestCqrsControlledLifetimeNames.StepName(value.ArmId, value.RequestId, value.Step))
            {
                throw InvalidProof();
            }
            steps.Add(value);
            return;
        }
        if (name.StartsWith(AckPrefix, StringComparison.Ordinal))
        {
            var value = RequestCqrsControlledLifetimeCodec.ReadAck(bytes);
            if (value.SessionId != sessionId || name != RequestCqrsControlledLifetimeNames.AckName(value.ArmId, value.RequestId, value.Step))
            {
                throw InvalidProof();
            }
            acks.Add(value);
            return;
        }
        var identity = RequestCqrsControlledLifetimeNames.ParsePart(name);
        if (identity.Index >= options.Value.MaximumFiles || bytes.Length <= EmptyBytes)
        {
            throw InvalidProof();
        }
        parts.Add(name, bytes);
    }

    internal void Require(IReadOnlyList<RequestCqrsProbeLoadedArm> arms, IReadOnlyList<RequestCqrsProbeMarkerRecord> markers)
    {
        foreach (var plan in plans)
        {
            var arm = arms.SingleOrDefault(value => value.Record.ArmId == plan.ArmId)?.Record ?? records.RetainedArm(plan.ArmId);
            RequireArm(plan, arm ?? throw InvalidProof());
        }
        foreach (var step in steps)
        {
            RequireStep(step, arms);
        }
        foreach (var ack in acks)
        {
            if (!steps.Any(step => step.ArmId == ack.ArmId && step.RequestId == ack.RequestId && step.Step == ack.Step))
            {
                throw InvalidProof();
            }
        }
        RequireParts();
        foreach (var marker in markers.Where(value => value.Phase == RequestCqrsProbePhase.ControlledLifetimeReplayBeforeDispatch))
        {
            var own = steps.Where(value => value.ArmId == marker.ArmId && value.RequestId == marker.RequestId).ToArray();
            if (!own.Any(value => value.Step == BaselineStep))
            {
                throw InvalidProof();
            }
            if (marker.Outcome == RequestCqrsProbeOutcome.Released)
            {
                var plan = plans.Single(value => value.ArmId == marker.ArmId);
                if (!acks.Any(value => value.ArmId == marker.ArmId && value.RequestId == marker.RequestId && value.Step == Last(plan)))
                {
                    throw InvalidProof();
                }
            }
        }
    }

    private void RequireStep(RequestCqrsControlledLifetimeStepWitness step, IReadOnlyList<RequestCqrsProbeLoadedArm> arms)
    {
        var plan = plans.SingleOrDefault(value => value.ArmId == step.ArmId) ?? throw InvalidProof();
        var arm = arms.SingleOrDefault(value => value.Record.ArmId == step.ArmId)?.Record ?? records.RetainedArm(step.ArmId);
        if (step.CommandId != plan.CommandId || step.PrincipalId != plan.PrincipalId || step.SourceArmId != plan.SourceArmId
            || step.Step > Last(plan) || arm is { } exactArm && step.Partition != exactArm.Partition)
        {
            throw InvalidProof();
        }
        if (step.Step == BaselineStep)
        {
            _ = RequestCqrsControlledLifetimeNativeBinding.RequireCarrier(step, Carrier(step));
            return;
        }
        var baseline = steps.SingleOrDefault(value => value.ArmId == step.ArmId && value.RequestId == step.RequestId && value.Step == BaselineStep)
            ?? throw InvalidProof();
        RequestCqrsControlledLifetimeBinding.RequireSame(baseline, step);
        if (!acks.Any(value => value.ArmId == step.ArmId && value.RequestId == step.RequestId && value.Step == step.Step - IndexStep))
        {
            throw InvalidProof();
        }
        var request = RequestCqrsControlledLifetimeParts.Join(parts, step.ArmId, step.RequestId, step.Step, RequestRole,
            step.RequestBytes, step.RequestParts, step.RequestSha256, options);
        var reply = RequestCqrsControlledLifetimeParts.Join(parts, step.ArmId, step.RequestId, step.Step, ReplyRole,
            step.ReplyBytes, step.ReplyParts, step.ReplySha256, options);
        var donor = step.Variation == ControlledLifetimeReplayVariation.Foreign
            ? Carrier(steps.Single(value => value.ArmId == step.SourceArmId && value.Step == BaselineStep)) : null;
        _ = RequestCqrsControlledLifetimeNativeBinding.RequireNegative(step, Carrier(baseline), request, reply, donor);
    }

    private void RequireParts()
    {
        foreach (var item in parts)
        {
            var part = RequestCqrsControlledLifetimeNames.ParsePart(item.Key);
            var plan = plans.SingleOrDefault(value => value.ArmId == part.Arm) ?? throw InvalidProof();
            if (part.Step > Last(plan) || (part.Step == BaselineStep) != (part.Role == CarrierRole))
            {
                throw InvalidProof();
            }
            var own = steps.SingleOrDefault(value => value.ArmId == part.Arm && value.RequestId == part.Request && value.Step == part.Step);
            if (part.Step > BaselineStep && !acks.Any(value => value.ArmId == part.Arm && value.RequestId == part.Request && value.Step == part.Step - IndexStep))
            {
                throw InvalidProof();
            }
            if (own is not null && part.Index >= RequestCqrsControlledLifetimeBinding.PartCount(own, part.Role))
            {
                throw InvalidProof();
            }
            if (part.Index > FirstIndex && !parts.ContainsKey(RequestCqrsControlledLifetimeNames.PartName(part.Arm, part.Request, part.Step, part.Role, part.Index - IndexStep)))
            {
                throw InvalidProof();
            }
        }
    }

    internal byte[] Donor(RequestCqrsProbeClaim claim, Guid donorId, IReadOnlyList<RequestCqrsProbeMarkerRecord> markers)
    {
        var plan = plans.SingleOrDefault(value => value.ArmId == donorId) ?? throw InvalidProof();
        var baseline = steps.SingleOrDefault(value => value.ArmId == donorId && value.Step == BaselineStep) ?? throw InvalidProof();
        if (plan.Variation != ControlledLifetimeReplayVariation.Substituted || plan.SourceArmId is not null
            || baseline.PrincipalId != claim.Identity.PrincipalId || baseline.Partition != claim.Arm.Record.Partition
            || !markers.Any(value => value.ArmId == donorId && value.RequestId == baseline.RequestId
                && value.Phase == RequestCqrsProbePhase.ControlledLifetimeReplayBeforeDispatch && value.Outcome == RequestCqrsProbeOutcome.Released)
            || !markers.Any(value => value.ArmId == donorId && value.RequestId == baseline.RequestId
                && value.Phase == RequestCqrsProbePhase.ProducerDisposed && value.Outcome == RequestCqrsProbeOutcome.Observed))
        {
            throw InvalidProof();
        }
        return Carrier(baseline);
    }

    private byte[] Carrier(RequestCqrsControlledLifetimeStepWitness step)
        => RequestCqrsControlledLifetimeParts.Join(parts, step.ArmId, step.RequestId, BaselineStep, CarrierRole,
            step.CarrierBytes, step.CarrierParts, step.CarrierSha256, options);

    internal static int Last(RequestCqrsControlledLifetimePlan plan)
        => plan.Variation == ControlledLifetimeReplayVariation.Foreign ? LastNegativeStep : SubstitutedStep;

    internal static void RequireArm(RequestCqrsControlledLifetimePlan plan, RequestCqrsProbeArmRecord arm)
    {
        if (plan.SessionId != arm.SessionId || plan.ArmId != arm.ArmId || plan.CommandId != arm.CommandId
            || plan.PrincipalId != arm.PrincipalId || plan.SourceArmId != arm.SourceArmId
            || arm.Phase != RequestCqrsProbePhase.ControlledLifetimeReplayBeforeDispatch
            || arm.Action != RequestCqrsProbeAction.Hold || arm.ReadKind is not null || arm.Partition is null)
        {
            throw InvalidProof();
        }
    }
    private static InvalidOperationException InvalidProof() => new(Invalid);
}
