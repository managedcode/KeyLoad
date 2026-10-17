using KeyLoad.Clustering;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class Bm25PortableMovementPurposeEvidence(int node,
    Bm25PortableMovementBorrow original) : IGrainPartitionMovementSealedOperationObserver, IGrainRequestPurposeObserver, IGrainRequestPhaseObserver, IGrainRequestNativePhaseObserver
{
    private readonly NativeMovementParentStateEvidence phases = new(node);
    private readonly Lock gate = new();
    private Scope? issued;
    private Rejection? rejected;

    public ValueTask BorrowAsync(ReplicatedOperation operation, CancellationToken cancellationToken)
        => original.BorrowAsync(operation, cancellationToken);

    public ValueTask ObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        global::Orleans.Runtime.IGrainContext context, CancellationToken cancellationToken)
        => phases.ObserveAsync(identity, phase, context, cancellationToken);

    public ValueTask ObserveNativeAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        ReplicaSiloDiscoveryState actualRuntime, CancellationToken cancellationToken)
        => phases.ObserveNativeAsync(identity, phase, actualRuntime, cancellationToken);

    public void ProducerDisposed(GrainRequestProbeIdentity identity, global::Orleans.Runtime.IGrainContext context)
        => phases.ProducerDisposed(identity, context);

    public string? BeforeSign(GrainRequestEnvelope actual)
    {
        lock (gate)
        {
            issued = Scope.Read(actual);
        }
        return null;
    }

    public void RejectedAfterMac(GrainRequestEnvelope actual, KeyLoadException failure, GrainRequestScopeCut cut)
    {
        lock (gate)
        {
            rejected ??= new(Scope.Read(actual), failure.Code, cut);
        }
    }

    internal void Write(TextWriter output, Exception primary)
    {
        Scope? last;
        Rejection? first;
        lock (gate)
        {
            last = issued;
            first = rejected;
        }
        var error = primary is KeyLoadException typed ? typed.Code.ToString() : primary.GetType().Name;
        var detail = primary is KeyLoadException failure && failure.Message == GrainRoutingProtocol.InvalidRequest
            ? GrainRoutingProtocol.InvalidRequest : "Other safe failure detail (not printed).";
        phases.Write(output);
        output.WriteLine($"Native movement node={node}; surfacedError={error}; safeDetail={detail}; issued={last}; rejected={first}");
    }

    private sealed record Scope(string PurposeLabel, int PurposeLength, Guid RequestId, Guid CommandId, Guid Incarnation,
        GrainReadKind? ReadKind, OperationKind? CommandKind, DateTimeOffset ExpiresAt)
    {
        internal static Scope Read(GrainRequestEnvelope actual) => new(
            "UnclassifiedPurpose", actual.Purpose.Length,
            actual.RequestId, actual.CommandId, actual.Incarnation, actual.ReadKind, actual.CommandKind, actual.ExpiresAt);
    }

    private sealed record Rejection(Scope Actual, ErrorCode Error, GrainRequestScopeCut Cut);
}
