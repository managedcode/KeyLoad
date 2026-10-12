using System.Security.Cryptography;
using System.Text;
using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeMovementParentStateEvidence(int configuredObserverOwnerIndex) : IGrainRequestPhaseObserver
{
    private readonly Lock gate = new();
    private Stage? executing;
    private Stage? returned;

    public ValueTask ObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        IGrainContext context, CancellationToken cancellationToken)
    {
        if (identity.ReadKind != GrainReadKind.PartitionMovementParentState
            || phase is not (GrainRequestPhase.MovementParentStateExecuting or GrainRequestPhase.MovementParentStateReturned))
        { return ValueTask.CompletedTask; }
        lock (gate)
        {
            if (phase == GrainRequestPhase.MovementParentStateExecuting && executing is null)
            { executing = Read(identity, context); }
            else if (phase == GrainRequestPhase.MovementParentStateReturned && returned is null
                && executing?.RequestId == identity.RequestId)
            { returned = Read(identity, context); }
        }
        return ValueTask.CompletedTask;
    }

    public void ProducerDisposed(GrainRequestProbeIdentity identity, IGrainContext context) { }

    internal void Write(TextWriter output)
    {
        Stage? before;
        Stage? after;
        lock (gate)
        { before = executing; after = returned; }
        output.WriteLine($"Native movement configuredObserverOwnerIndex={configuredObserverOwnerIndex}; Executing={before}; Returned={after}");
    }

    private static Stage Read(GrainRequestProbeIdentity identity, IGrainContext context)
        => new(identity.RequestId, identity.ReadKind,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(context.GrainId.ToString()))),
            context.ActivationId.ToParsableString());

    private sealed record Stage(Guid RequestId, GrainReadKind? ReadKind, string GrainIdDigest, string ActivationId);
}
