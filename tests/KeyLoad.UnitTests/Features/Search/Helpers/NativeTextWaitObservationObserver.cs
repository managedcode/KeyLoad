using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextWaitObservationObserver(ZoneTreeStore store) : IQueryReadCutObserver
{
    internal WaitForIndexReadCutObservation? Value { get; private set; }
    internal string[]? Image { get; private set; }
    internal int Count { get; private set; }

    public void Observe(QueryReadCutObservation observation, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Scalar observation is outside this native wait case.");

    public void Observe(WaitForIndexReadCutObservation observation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Count != 0)
        { throw new InvalidOperationException("The native wait observation exceeded its original single-call bound."); }
        Image = QueueWholeFlowStorage.Bytes(store);
        Value = observation;
        Count++;
    }
}
