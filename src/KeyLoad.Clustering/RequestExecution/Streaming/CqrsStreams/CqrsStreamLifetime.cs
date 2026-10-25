using System.Runtime.CompilerServices;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

/// <summary>Owns request stream admission, the execution child enumerator and activation settlement.</summary>
internal static class CqrsStreamLifetime
{
    /// <summary>Creates the lazy bounded stream wrapper without starting capability work.</summary>
    /// <param name="createStream">Creates the execution CQRS producer with the linked execution token.</param>
    /// <param name="serializer">The actual registered execution chunk serializer.</param>
    /// <param name="requestId">The request grain's GUID key.</param>
    /// <param name="clock">Runtime clock for the finite execution deadline.</param>
    /// <param name="settled">Activation cleanup to run after execution producer disposal completes.</param>
    /// <param name="cancellationToken">The request RPC cancellation token.</param>
    /// <param name="options">The same centrally validated routing snapshot used by the request owner.</param>
    /// <param name="owner">Optional silo-local owner for server request work.</param>
    /// <returns>The lazy bounded execution chunk stream.</returns>
    internal static IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> Run(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        Guid requestId, TimeProvider clock, Action settled, IOptions<GrainRoutingOptions> options,
        RequestWorkOwner? owner, CancellationToken cancellationToken)
        => RunCore(createStream, serializer, requestId, clock, () => { settled(); return ValueTask.CompletedTask; }, owner, options, options.Value.ExecutionLifetime,
            null, cancellationToken, CancellationToken.None);

    internal static IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> RunWithPurpose(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        Guid requestId, TimeProvider clock, Action settled, IOptions<GrainRoutingOptions> options,
        RequestWorkOwner? owner, CqrsStreamPurpose purpose, CancellationToken cancellationToken)
        => RunCore(createStream, serializer, requestId, clock, () => { settled(); return ValueTask.CompletedTask; }, owner, options, options.Value.ExecutionLifetime,
            purpose, cancellationToken, CancellationToken.None);

    internal static IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> RunWithAwaitedSettlement(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        Guid requestId, TimeProvider clock, Func<ValueTask> settled, IOptions<GrainRoutingOptions> options,
        RequestWorkOwner? owner, CqrsStreamPurpose purpose, CancellationToken cancellationToken)
        => RunCore(createStream, serializer, requestId, clock, settled, owner, options, options.Value.ExecutionLifetime,
            purpose, cancellationToken, CancellationToken.None);

    internal static IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> RunBoundWithAwaitedSettlement(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        Guid requestId, TimeProvider clock, Func<ValueTask> settled, IOptions<GrainRoutingOptions> options,
        RequestWorkOwner owner, CqrsStreamPurpose purpose,
        Func<RequestWorkLease> acquireVerified, CancellationToken cancellationToken)
        => BoundCqrsStreamLifetime.Run(createStream, serializer, requestId, clock, settled, options,
            owner, purpose, acquireVerified, cancellationToken);

    private static async IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> RunCore(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        Guid requestId, TimeProvider clock, Func<ValueTask> settled, RequestWorkOwner? owner, IOptions<GrainRoutingOptions> options, TimeSpan executionLifetime,
        CqrsStreamPurpose? purpose, CancellationToken cancellationToken, [EnumeratorCancellation] CancellationToken enumerationToken)
    {
        using var deadline = new CancellationTokenSource(executionLifetime, clock);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, enumerationToken);
        using var ownerRequest = owner is null ? null
            : CancellationTokenSource.CreateLinkedTokenSource(request.Token, owner.ShutdownToken);
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(ownerRequest?.Token ?? request.Token,
            deadline.Token);
        var admission = new CqrsStreamAdmission(serializer: serializer, requestId: requestId, options: options, purpose: purpose);
        var workLease = await RequestWorkStreamSettlement.AcquireOrSettleAwaitedAsync(owner, requestId, settled)
            .ConfigureAwait(true);
        IAsyncEnumerator<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>? enumerator = null;
        Exception? primary = null;
        try
        {
            var creation = CreateEnumerator(createStream, execution.Token);
            enumerator = creation.Enumerator;
            primary = CreationFailure(creation);
            while (primary is null && enumerator is not null)
            {
                var step = await ReadNextAsync(enumerator, admission, execution.Token).ConfigureAwait(true);
                if (step.Error is not null || !step.HasItem)
                {
                    primary = step.Error;
                    break;
                }

                yield return step.Item!;
            }

            primary ??= admission.CompletionFailure(execution.Token);
        }
        finally
        {
            var settlementFailure = await RequestWorkStreamSettlement.SettleAndReleaseAwaitedAsync(enumerator,
                settled, primary, workLease).ConfigureAwait(true);
            RequestWorkSettlement.Rethrow(settlementFailure);
        }

        RequestWorkSettlement.Rethrow(primary);
    }

    private static Exception? CreationFailure(CqrsEnumeratorCreation creation)
        => creation.Error ?? (creation.Enumerator is null
            ? Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest) : null);

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
            admission.Admit(item, cancellationToken);
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
