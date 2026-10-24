using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Search;

internal sealed class TextWaitThrowingObservationObserver(ZoneTreeStore store, IOException failure)
    : IQueryReadCutObserver
{
    internal TextWaitObservationObserver Capture { get; } = new(store);

    public void Observe(QueryReadCutObservation observation, CancellationToken cancellationToken)
        => Capture.Observe(observation, cancellationToken);

    public void Observe(WaitForIndexReadCutObservation observation, CancellationToken cancellationToken)
    {
        Capture.Observe(observation, cancellationToken);
        throw failure;
    }
}
