namespace KeyLoad.Comparisons;

internal static class OpenLoopOwnerCancellation
{
    internal static async Task<Exception?> CancelAsync(CancellationTokenSource owner)
    {
        try
        {
            await owner.CancelAsync().ConfigureAwait(false);
            return null;
        }
        catch (Exception failure)
        {
            return failure;
        }
    }
}
