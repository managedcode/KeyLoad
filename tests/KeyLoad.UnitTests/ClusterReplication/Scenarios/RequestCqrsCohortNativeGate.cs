namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class RequestCqrsCohortNativeGate
{
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource aborted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Entered => entered.Task;
    internal Task Aborted => aborted.Task;
    internal async Task WaitAsync(CancellationToken token)
    {
        entered.TrySetResult();
        try
        {
            await released.Task.WaitAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { aborted.TrySetResult(); throw; }
    }
    internal void Release() => released.TrySetResult();
}
