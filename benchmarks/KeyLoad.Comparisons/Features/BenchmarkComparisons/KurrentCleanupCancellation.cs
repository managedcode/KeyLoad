namespace KeyLoad.Comparisons.Targets;

internal sealed class KurrentCleanupCancellation(CancellationToken token) : IDisposable
{
    private Task originalCompletion = Task.CompletedTask;

    internal CancellationTokenSource Operations { get; } = CancellationTokenSource.CreateLinkedTokenSource(token);
    internal CancellationTokenSource Deadline { get; } = new(TimeSpan.FromSeconds(KurrentConstants.CleanupTimeoutSeconds));

    internal void CloseAfter(Task originalCompletion)
        => this.originalCompletion = Task.WhenAll(this.originalCompletion, originalCompletion);

    public void Dispose()
    {
        if (originalCompletion.IsCompleted)
        {
            _ = originalCompletion.Exception;
            DisposeSources();
            return;
        }
        _ = originalCompletion.ContinueWith(completed =>
        {
            _ = completed.Exception;
            DisposeSources();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void DisposeSources()
    {
        Operations.Dispose();
        Deadline.Dispose();
    }
}
