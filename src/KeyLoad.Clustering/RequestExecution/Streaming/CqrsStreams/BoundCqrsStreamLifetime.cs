using System.Runtime.CompilerServices;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

/// <summary>Retains the original verified producer and borrows its exact operation result ledger.</summary>
internal static class BoundCqrsStreamLifetime
{
    internal static async IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> Run(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer,
        Guid requestId, TimeProvider clock, Func<ValueTask> settled, IOptions<GrainRoutingOptions> options,
        RequestWorkOwner owner, CqrsStreamPurpose purpose,
        Func<RequestWorkLease> acquireVerified, CancellationToken cancellationToken,
        [EnumeratorCancellation] CancellationToken enumerationToken = default)
    {
        var work = new BoundCqrsStreamWork();
        var admission = new CqrsStreamAdmission(serializer, requestId, options, purpose);
        IAsyncEnumerator<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>? enumerator = null;
        Exception? primary = null;
        try
        {
            work.Start(clock, options, owner, purpose, acquireVerified,
                cancellationToken, enumerationToken);
            var creation = Create(createStream, work.Token);
            enumerator = creation.Enumerator;
            primary = creation.Error;
            while (primary is null && enumerator is not null)
            {
                var step = await ReadNextAsync(enumerator, admission, work).ConfigureAwait(true);
                if (step.Error is not null || !step.HasItem)
                {
                    primary = step.Error;
                    break;
                }
                yield return step.Item!;
            }
            primary ??= admission.CompletionFailure(work.Token);
        }
        finally
        {
            work.PrepareSettlement(enumerator, settled, primary);
            await work.DisposeAsync().ConfigureAwait(true);
        }
        RequestWorkSettlement.Rethrow(primary);
    }

    private static CqrsEnumeratorCreation Create(
        Func<CancellationToken, IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> createStream,
        CancellationToken token)
    {
        try
        {
            var enumerator = createStream(token).GetAsyncEnumerator(token);
            return new(enumerator, enumerator is null
                ? Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest) : null);
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

    private static async ValueTask<CqrsReadStep<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>> ReadNextAsync(
        IAsyncEnumerator<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> enumerator,
        CqrsStreamAdmission admission, BoundCqrsStreamWork work)
    {
        try
        {
            work.Token.ThrowIfCancellationRequested();
            if (!await enumerator.MoveNextAsync().ConfigureAwait(true))
            {
                return new(false, null, null);
            }
            var item = enumerator.Current;
            admission.Admit(item, work.Token);
            work.Token.ThrowIfCancellationRequested();
            work.AdmitPublication(item.Kind);
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
