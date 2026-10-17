using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextWaitThrowingObservationObserver(ZoneTreeStore store, IOException failure)
    : IQueryReadCutObserver
{
    internal NativeTextWaitObservationObserver Capture { get; } = new(store);

    public void Observe(QueryReadCutObservation observation, CancellationToken cancellationToken)
        => Capture.Observe(observation, cancellationToken);

    public void Observe(WaitForIndexReadCutObservation observation, CancellationToken cancellationToken)
    {
        Capture.Observe(observation, cancellationToken);
        throw failure;
    }
}
