using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class OpenLoopOriginalFailureReadFixture : IAsyncDisposable
{
    private readonly TaskCompletionSource<OpenLoopCancellationHealthRead> read = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource disposalStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int disposeCalls;

    internal OpenLoopOriginalFailureReadFixture()
    {
        Completion = OpenLoopHealthyReadVerifier.SettleReadAsync(read.Task, StartDispose);
    }

    internal Task<OpenLoopCancellationHealthRead> Completion { get; }
    internal Task DisposalStarted => disposalStarted.Task;
    internal int DisposeCalls => Volatile.Read(ref disposeCalls);
    internal bool ReadCompletedBeforeDispose { get; private set; }
    internal bool ReadWasCanceled => read.Task.IsCanceled;
    internal bool DisposalCompleted => disposal.Task.IsCompleted;

    internal void FailRead(Exception failure) => read.SetException(failure);
    internal void CompleteRead(OpenLoopCancellationHealthRead result) => read.SetResult(result);
    internal void CancelRead(CancellationToken cancellationToken) => read.SetCanceled(cancellationToken);
    internal void FailDisposal(Exception failure) => disposal.SetException(failure);
    internal void CompleteDisposal() => disposal.SetResult();

    public async ValueTask DisposeAsync()
    {
        read.TrySetCanceled();
        disposal.TrySetCanceled();
        await OpenLoopFailure.ObserveAsync(Completion).ConfigureAwait(false);
    }

    private ValueTask StartDispose()
    {
        ReadCompletedBeforeDispose = read.Task.IsCompleted;
        Interlocked.Increment(ref disposeCalls);
        disposalStarted.TrySetResult();
        return new(disposal.Task);
    }
}
