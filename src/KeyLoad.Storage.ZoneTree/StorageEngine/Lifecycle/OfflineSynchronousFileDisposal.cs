namespace KeyLoad.Storage.ZoneTree.IO;

/// <summary>Completes the published synchronous file close inline while preserving its original failures.</summary>
internal static class OfflineSynchronousFileDisposal
{
    internal static void Complete(TaskCompletionSource completion, Action flush, Action<Exception?> close)
    {
        Exception? primary = null;
        try
        {
            try
            {
                flush();
            }
            catch (Exception failure)
            {
                primary = failure;
                throw;
            }
            finally
            {
                close(primary);
            }
            completion.SetResult();
        }
        catch (Exception failure)
        {
            completion.SetException(failure);
            _ = completion.Task.Exception;
            throw;
        }
    }
}
