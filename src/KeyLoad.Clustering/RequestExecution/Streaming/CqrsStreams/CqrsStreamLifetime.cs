using System.Runtime.CompilerServices;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

/// <summary>Owns request stream admission, the execution child enumerator and activation settlement.</summary>
internal static class CqrsStreamLifetime
{
    /// <summary>Creates the lazy bounded stream wrapper without starting capability work.</summary>
    /// <param name="createStream">Creates the execution CQRS producer with the linked execution token.</param>
    /// <param name="requestId">The request grain's GUID key.</param>
    /// <param name="settled">Activation cleanup to run after execution producer disposal completes.</param>
    /// <param name="cancellationToken">The request RPC cancellation token.</param>
    /// <param name="owner">Optional silo-local owner for server request work.</param>
    /// <returns>The lazy bounded execution chunk stream.</returns>
    internal static IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> Run(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        Guid requestId, Action settled, RequestWorkOwner? owner, CancellationToken cancellationToken)
        => RunCore(createStream, requestId, () => { settled(); return ValueTask.CompletedTask; }, owner,
            null, cancellationToken, CancellationToken.None);

    internal static IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> RunWithPurpose(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        Guid requestId, Action settled, RequestWorkOwner? owner, CqrsStreamPurpose purpose,
        CancellationToken cancellationToken)
        => RunCore(createStream, requestId, () => { settled(); return ValueTask.CompletedTask; }, owner,
            purpose, cancellationToken, CancellationToken.None);

    internal static IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> RunWithAwaitedSettlement(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        Guid requestId, Func<ValueTask> settled, RequestWorkOwner? owner, CqrsStreamPurpose purpose,
        CancellationToken cancellationToken)
        => RunCore(createStream, requestId, settled, owner, purpose, cancellationToken, CancellationToken.None);

    internal static IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> RunBoundWithAwaitedSettlement(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        Guid requestId, Func<ValueTask> settled, RequestWorkOwner owner, CqrsStreamPurpose purpose,
        Func<RequestWorkLease> acquireVerified, CancellationToken cancellationToken)
        => BoundCqrsStreamLifetime.Run(createStream, requestId, settled, owner, purpose, acquireVerified,
            cancellationToken);

    private static async IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> RunCore(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        Guid requestId, Func<ValueTask> settled, RequestWorkOwner? owner,
        CqrsStreamPurpose? purpose, CancellationToken cancellationToken, [EnumeratorCancellation] CancellationToken enumerationToken)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, enumerationToken);
        using var execution = owner is null ? null
            : CancellationTokenSource.CreateLinkedTokenSource(request.Token, owner.ShutdownToken);
        var token = execution?.Token ?? request.Token;
        var admission = new CqrsStreamAdmission(requestId, purpose);
        var workLease = (await RequestWorkStreamSettlement.AcquireOrSettleAwaitedAsync(owner, requestId, settled)
            .ConfigureAwait(true)).Value;
        IAsyncEnumerator<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>? enumerator = null;
        Exception? primary = null;
        try
        {
            var creation = CreateEnumerator(createStream, token);
            enumerator = creation.Enumerator;
            primary = (CreationFailure(creation)).Value;
            while (primary is null && enumerator is not null)
            {
                var step = await ReadNextAsync(enumerator, admission, token).ConfigureAwait(true);
                if (step.Error is not null || !step.HasItem)
                {
                    primary = step.Error;
                    break;
                }

                yield return step.Item!;
            }

            primary ??= (admission.CompletionFailure(token)).Value;
        }
        finally
        {
            var settlementFailure = await RequestWorkStreamSettlement.SettleAndReleaseAwaitedAsync(enumerator,
                settled, primary, workLease).ConfigureAwait(true);
            RequestWorkSettlement.Rethrow(settlementFailure);
        }

        RequestWorkSettlement.Rethrow(primary);
    }

    private static (bool HasValue, Exception? Value) CreationFailure(CqrsEnumeratorCreation creation)
        => ((Exception?)(creation.Error ?? (creation.Enumerator is null
            ? Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest) : null))) is { } outcomeValue1 ? (true, outcomeValue1) : (false, default(Exception?));

    private static CqrsEnumeratorCreation CreateEnumerator(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        CancellationToken cancellationToken)
    {
        try
        {
            var enumerator = createStream(cancellationToken).GetAsyncEnumerator(cancellationToken);
            return new(enumerator, null);
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            return new(null, error);
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            return new(null, error);
        }
    }

    private static async ValueTask<CqrsReadStep<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>
        ReadNextAsync(IAsyncEnumerator<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> enumerator,
            CqrsStreamAdmission admission, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await enumerator.MoveNextAsync().ConfigureAwait(true))
            {
                return new(false, null, null);
            }

            var item = enumerator.Current;
            admission.Admit(item);
            cancellationToken.ThrowIfCancellationRequested();
            return new(true, item, null);
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            return new(false, null, error);
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            return new(false, null, error);
        }
    }
}
