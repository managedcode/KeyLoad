namespace KeyLoad.Comparisons;

internal static class OpenLoopOwnerCancellation
{
    internal static Task<Exception?> CancelAsync(CancellationTokenSource owner)
        => OpenLoopFailure.ObserveAsync(RequestCancellationAsync(owner));

    private static async Task RequestCancellationAsync(CancellationTokenSource owner)
        => await owner.CancelAsync().ConfigureAwait(false);
}
