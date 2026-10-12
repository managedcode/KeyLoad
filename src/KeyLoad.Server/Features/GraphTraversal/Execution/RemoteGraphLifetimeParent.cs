using KeyLoad.Core;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.GraphTraversal;

internal sealed class RemoteGraphLifetimeParent(IRemoteGraphParent original, NativeRequestWorkOwner owner)
    : IRemoteGraphParent
{
    public async Task<RemoteGraphEdgeResultV1> ExecuteAsync(GrainRequestEnvelope trusted, OperationKind kind,
        ReadOnlyMemory<byte> payload, ReadExecutionBudget work, CancellationToken caller)
    {
        ArgumentNullException.ThrowIfNull(trusted);
        ArgumentNullException.ThrowIfNull(work);
        using var operation = owner.Acquire(trusted.RequestId, NativeRequestWorkKind.RequestProducer);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, owner.ShutdownToken);
        using var stage = work.EnterStageCancellation(linked.Token);
        linked.Token.ThrowIfCancellationRequested();
        work.Check();
        var result = await original.ExecuteAsync(trusted, kind, payload, work, linked.Token).ConfigureAwait(false);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            work.Check();
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteGraphNativeProtocol.Unavailable, failure); }
        return result;
    }

    public async Task<RemoteIncomingGraphEdgesPageV1> ReadIncomingAsync(GrainRequestEnvelope trusted,
        ReadRemoteIncomingGraphEdgesRequestV1 request, ReadExecutionBudget work, CancellationToken caller)
    {
        ArgumentNullException.ThrowIfNull(trusted);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(work);
        using var operation = owner.Acquire(trusted.RequestId, NativeRequestWorkKind.RequestProducer);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, owner.ShutdownToken);
        using var stage = work.EnterStageCancellation(linked.Token);
        linked.Token.ThrowIfCancellationRequested();
        work.Check();
        var result = await original.ReadIncomingAsync(trusted, request, work, linked.Token).ConfigureAwait(false);
        linked.Token.ThrowIfCancellationRequested();
        work.Check();
        return result;
    }
}
