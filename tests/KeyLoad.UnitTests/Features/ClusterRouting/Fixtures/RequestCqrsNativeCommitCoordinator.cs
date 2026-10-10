using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsNativeCommitCoordinator(TestDatabase owner) : ICommitCoordinator
{
    public Task<OperationResult> SubmitAsync(OperationKind kind, Guid id, string principalId, string payloadJson,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(owner.SubmitIssuedEmbedded(new(id, kind, principalId, default, payloadJson), cancellationToken));
    }

    public Task<OperationResult> SubmitNativeAsync(OperationKind kind, Guid id, string principalId,
        ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var original = owner.Database.CreateNativeOperation(kind, id, principalId, default, payload);
        return Task.FromResult(owner.SubmitIssuedEmbedded(original, cancellationToken));
    }

    public Task<OperationResult> SubmitVerifiedAsync(ReplicatedOperation operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var verified = owner.Database.VerifyOperationAuthority(operation);
        return Task.FromResult(owner.SubmitIssuedEmbedded(verified, cancellationToken));
    }

    public Task ReadBarrierAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        owner.JoinNativeReadBarrier(cancellationToken);
        return Task.CompletedTask;
    }
}
