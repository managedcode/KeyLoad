using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class Bm25PortableMovementPurposeEvidence(int node,
    Bm25PortableMovementBorrow original) : IGrainPartitionMovementSealedOperationObserver, IGrainRequestPurposeObserver
{
    private readonly Lock gate = new();
    private Scope? issued;
    private Rejection? rejected;

    public ValueTask BorrowAsync(ReplicatedOperation operation, CancellationToken cancellationToken)
        => original.BorrowAsync(operation, cancellationToken);

    public string? BeforeSign(GrainRequestEnvelope actual)
    {
        lock (gate)
        { issued = Scope.Read(actual); }
        return null;
    }

    public void RejectedAfterMac(GrainRequestEnvelope actual, KeyLoadException failure, GrainRequestScopeCut cut)
    {
        lock (gate)
        { rejected ??= new(Scope.Read(actual), failure.Code, cut); }
    }

    internal void Write(TextWriter output, Exception primary)
    {
        Scope? last;
        Rejection? first;
        lock (gate)
        { last = issued; first = rejected; }
        var error = primary is KeyLoadException typed ? typed.Code.ToString() : primary.GetType().Name;
        var detail = primary is KeyLoadException failure && failure.Message == GrainRoutingProtocol.InvalidRequest
            ? GrainRoutingProtocol.InvalidRequest : "Other safe failure detail (not printed).";
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
