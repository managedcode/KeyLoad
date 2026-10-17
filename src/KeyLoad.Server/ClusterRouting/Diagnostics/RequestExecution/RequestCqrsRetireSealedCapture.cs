using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Retains only bounded original native bytes until their immutable private arm is explicitly retired.</summary>
internal sealed class RequestCqrsRetireSealedCapture(RequestCqrsProbeFiles files, IOptions<RequestProbeExecutionOptions> options)
{
    private readonly Lock sync = new();
    private readonly Dictionary<Guid, byte[]> originals = [];

    internal void Capture(RequestCqrsProbeLifecycle lifecycle,
        GrainRequestProbeIdentity identity, ReplicatedOperation original, IGrainContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lifecycle.EnterCallback();
        try
        {
            var claim = lifecycle.FindClaim(identity.RequestId);
            if (claim?.Arm.Record.Phase != RequestCqrsProbePhase.RetireOperationSealed)
            {
                return;
            }
            files.RequireActiveArm(claim.Arm);
            if (claim.Identity != identity || identity.CommandKind != OperationKind.PartitionMovementPhase
                || claim.Arm.Record.Action != RequestCqrsProbeAction.Hold || claim.Arm.Record.ReadKind is not null
                || original.Id != identity.CommandId || original.PrincipalId != identity.PrincipalId
                || RequestContext.Get(GrainRequestStreamProtocol.ContextKey) is not GrainRequestContextState current
                || current.RequestId != identity.RequestId || current.CommandId != identity.CommandId
                || current.ConnectionId != claim.ConnectionId)
            {
                throw NativeRetireCutWire.Invalid();
            }
            if (NativeSerialization.Measure(original) > options.Value.MaximumRecordBytes)
            {
                throw NativeCanonicalModelCutCodec.Exhausted();
            }
            var bytes = NativeSerialization.Serialize(original);
            lock (sync)
            {
                Prune();
                if (originals.ContainsKey(claim.Arm.Record.ArmId))
                {
                    throw NativeRetireCutWire.Invalid();
                }
                if (bytes.Length > options.Value.MaximumRecordBytes || originals.Count >= options.Value.MaximumArms
                    || originals.Values.Sum(value => (long)value.Length) + bytes.Length > options.Value.MaximumAggregateBytes)
                {
                    throw NativeCanonicalModelCutCodec.Exhausted();
                }
                originals.Add(claim.Arm.Record.ArmId, bytes);
            }
        }
        finally
        {
            lifecycle.ExitCallback();
        }
    }

    internal ReadOnlyMemory<byte>? Read(NativeRetireCutRequest request)
    {
        lock (sync)
        {
            Prune();
            return originals.TryGetValue(request.ArmId, out var bytes) ? bytes.ToArray() : null;
        }
    }

    private void Prune()
    {
        var active = files.ReadSnapshot().Arms.Select(arm => arm.Record.ArmId).ToHashSet();
        foreach (var retired in originals.Keys.Where(id => !active.Contains(id)).ToArray())
        {
            originals.Remove(retired);
        }
    }
}
