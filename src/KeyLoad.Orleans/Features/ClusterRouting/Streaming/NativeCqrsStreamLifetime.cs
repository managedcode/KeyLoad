using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using ManagedCode.Communication.CQRS;
using Orleans.Serialization;

namespace KeyLoad.Orleans;

/// <summary>Owns request stream admission, the native child enumerator and activation settlement.</summary>
internal static class NativeCqrsStreamLifetime
{
    /// <summary>Creates the lazy bounded stream wrapper without starting capability work.</summary>
    /// <param name="createStream">Creates the native CQRS producer with the linked execution token.</param>
    /// <param name="serializer">The actual registered native chunk serializer.</param>
    /// <param name="requestId">The request grain's GUID key.</param>
    /// <param name="clock">Runtime clock for the finite execution deadline.</param>
    /// <param name="settled">Activation cleanup to run after native producer disposal completes.</param>
    /// <param name="cancellationToken">The request RPC cancellation token.</param>
    /// <returns>The lazy bounded native chunk stream.</returns>
    internal static IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> Run(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        Guid requestId, TimeProvider clock, Action settled, CancellationToken cancellationToken)
        => RunCore(createStream, serializer, requestId, clock, settled, cancellationToken, CancellationToken.None);

    private static async IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> RunCore(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        Guid requestId, TimeProvider clock, Action settled, CancellationToken cancellationToken,
        [EnumeratorCancellation] CancellationToken enumerationToken)
    {
        using var deadline = new CancellationTokenSource(GrainRequestStreamProtocol.ExecutionLifetime, clock);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, enumerationToken);
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(request.Token, deadline.Token);
        var admission = new NativeCqrsStreamAdmission(serializer, requestId);
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
                if (step.Error is not null)
                {
                    primary = step.Error;
                    break;
                }

                if (!step.HasItem)
                {
                    break;
                }

                yield return step.Item!;
            }

            if (primary is null)
            {
                primary = admission.CompletionFailure(execution.Token);
            }
        }
        finally
        {
            var settlementFailure = await NativeCqrsStreamSettlement.SettleAsync(enumerator, settled, primary)
                .ConfigureAwait(true);
            if (settlementFailure is not null)
            {
                ExceptionDispatchInfo.Capture(settlementFailure).Throw();
            }
        }

        if (primary is not null)
        {
            ExceptionDispatchInfo.Capture(primary).Throw();
        }
    }

    private static Exception? CreationFailure(NativeCqrsEnumeratorCreation creation)
        => creation.Error ?? (creation.Enumerator is null
            ? Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest) : null);

    private static NativeCqrsEnumeratorCreation CreateEnumerator(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        CancellationToken cancellationToken)
    {
        try
        {
            var enumerator = createStream(cancellationToken).GetAsyncEnumerator(cancellationToken);
            return new(enumerator, null);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return new(null, error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return new(null, error);
        }
    }

    private static async ValueTask<NativeCqrsReadStep<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>
        ReadNextAsync(IAsyncEnumerator<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> enumerator,
            NativeCqrsStreamAdmission admission, CancellationToken cancellationToken)
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
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return new(false, null, error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return new(false, null, error);
        }
    }
}

internal readonly record struct NativeCqrsReadStep<T>(bool HasItem, T? Item, Exception? Error);
internal readonly record struct NativeCqrsEnumeratorCreation(
    IAsyncEnumerator<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>? Enumerator, Exception? Error);
