using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Consumes only the stored original target frame; no caller-supplied operation is accepted.</summary>
internal static class NativeCommandObservationRead
{
    internal static async Task<T> ExecuteAsync<T>(DatabaseEngine database,
        ICommitCoordinator coordinator, NativeRequestWorkOwner owner, PhysicalShardRecord physical,
        ReplicaSiloDiscovery runtime, string opaqueToken, IOptions<GrainRoutingOptions> options,
        Func<CancellationToken, Task> admitPeer,
        Func<GrainOperationReply, ReadExecutionBudget, T> publish, Action<T> clearOnFailure, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var handle = NativeCommandObservationHandleCodec.Verify(database, opaqueToken);
        var publication = new NativeCommandObservationPublication<T>(publish, clearOnFailure);
        var original = owner.ConsumeCommandObservation(handle, physical, runtime, database);
        return await ExecuteOriginalAsync(database, coordinator, original, options, admitPeer, publication, token).ConfigureAwait(true);
    }

    private static async Task<T> ExecuteOriginalAsync<T>(DatabaseEngine database,
        ICommitCoordinator coordinator, NativeCommandObservation original,
        IOptions<GrainRoutingOptions> options, Func<CancellationToken, Task> admitPeer,
        NativeCommandObservationPublication<T> publication, CancellationToken token)
    {
        CancellationTokenSource? linked = null;
        ReadExecutionBudgetStageCancellationLease? stage = null;
        Exception? primary = null;
        Exception? cleanup = null;
        KeyLoadException? refusal = null;
        try
        {
            linked = CancellationTokenSource.CreateLinkedTokenSource(original.Token, token);
            stage = original.Budget.EnterStageCancellation(linked.Token);
            original.Budget.Check();
            await admitPeer(linked.Token).ConfigureAwait(true);
            original.Budget.Check();
            await coordinator.ReadBarrierAsync(linked.Token).ConfigureAwait(true);
            original.Budget.Check();
            PublishOutcome(database, original, options, publication, ref refusal, linked.Token);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            primary = NativeCommandObservationCompletion.Retain(refusal, error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            primary = NativeCommandObservationCompletion.Retain(refusal, error);
        }
        finally
        {
            cleanup = Dispose(stage, cleanup);
            try
            {
                linked?.Dispose();
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                cleanup = NativeCommandObservationCompletion.Retain(cleanup, error);
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                cleanup = NativeCommandObservationCompletion.Retain(cleanup, error);
            }
        }
        var failure = await NativeCommandObservationCompletion.CompleteAsync(original,
            primary ?? refusal, cleanup, refusal).ConfigureAwait(true);
        if (failure is not null)
        {
            failure = publication.Clear(failure);
            NativeRequestWorkSettlement.Rethrow(failure);
        }
        return publication.Value;
    }

    private static void PublishOutcome<T>(DatabaseEngine database, NativeCommandObservation original,
        IOptions<GrainRoutingOptions> options, NativeCommandObservationPublication<T> publication,
        ref KeyLoadException? refusal, CancellationToken token)
    {
        OperationResult result;
        try
        {
            result = database.ResolveSubmissionOutcome(original.Intent, original.Budget);
        }
        catch (KeyLoadException error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            refusal = error;
            result = new(null, error.Code, error.Message);
        }
        var observed = new NativeCommandObservationResultV1(result, original.Budget.ReadBytes,
            original.Budget.ConstrainedNativeRecords, [.. original.Lease.Dependencies]);
        var reply = NativeCommandObservationEncoding.Encode(observed, original.Budget, options, token);
        _ = publication.Capture(reply, original.Budget);
        original.Budget.Check();
        original.Lease.AdmitPublication();
    }

    private static Exception? Dispose(IDisposable? owned, Exception? previous)
    {
        try
        {
            owned?.Dispose();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return NativeCommandObservationCompletion.Retain(previous, error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return NativeCommandObservationCompletion.Retain(previous, error);
        }
        return previous;
    }

}
