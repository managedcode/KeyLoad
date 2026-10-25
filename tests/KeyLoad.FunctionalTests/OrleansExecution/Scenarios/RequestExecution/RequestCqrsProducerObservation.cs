namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class RequestCqrsProducerObservation
{
    internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource ProducerSettled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource ActivationSettled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int ProducerSettlementCount
    {
        get; private set;
    }
    internal int ActivationSettlementCount
    {
        get; private set;
    }
    internal bool CancellationObserved
    {
        get; private set;
    }

    internal void MarkStarted() => Started.TrySetResult();

    internal void MarkProducerSettled(bool cancellationObserved)
    {
        ProducerSettlementCount++;
        CancellationObserved = cancellationObserved;
        ProducerSettled.TrySetResult();
    }

    internal void MarkActivationSettled()
    {
        ActivationSettlementCount++;
        ActivationSettled.TrySetResult();
    }
}
