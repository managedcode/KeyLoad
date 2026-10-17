using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextCapturedOriginalSettlement
{
    internal static async Task JoinAsync(Task<RankedDocument[]> first, Task<RankedDocument[]>? original,
        TaskCompletionSource firstRelease, TaskCompletionSource release,
        CancellationTokenSource firstCancellation, CancellationTokenSource originalCancellation,
        bool cancelOriginal, List<Exception> failures)
    {
        try
        {
            firstRelease.TrySetResult();
            release.TrySetResult();
            if (!first.IsCompleted)
            {
                await ServerFailureObserver.ObserveAsync(firstCancellation.CancelAsync, failures);
            }
            await JoinOriginalAsync(original, originalCancellation, cancelOriginal, failures);
        }
        finally
        {
            try
            {
                _ = await first;
            }
            catch (Exception error) when (KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                RetainFailure(error, failures);
            }
            catch (Exception error) when (!KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                RetainFailure(error, failures);
            }
        }
    }

    internal static async Task JoinOriginalAsync(Task<RankedDocument[]>? original,
        CancellationTokenSource caller, bool cancelOriginal, List<Exception> failures)
    {
        if (original is null)
        {
            return;
        }
        if (!original.IsCompleted)
        {
            await ServerFailureObserver.ObserveAsync(caller.CancelAsync, failures);
        }
        try
        {
            _ = await original;
        }
        catch (OperationCanceledException) when (cancelOriginal && failures.Count == 0)
        {
        }
        catch (Exception error) when (KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            RetainFailure(error, failures);
        }
        catch (Exception error) when (!KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            RetainFailure(error, failures);
        }
    }

    internal static void RetainFailure(Exception error, List<Exception> failures)
    {
        if (!failures.Any(original => ReferenceEquals(original, error)))
        {
            failures.Add(error);
        }
    }
}
