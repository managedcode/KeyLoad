namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeCqrsObservationGrain : Grain,
    INativeCqrsObservationSinkGrain,
    INativeCqrsObservationReaderGrain
{
    private readonly List<string> events = [];
    private NativeCqrsLeafObservation? leafObservation;
    private bool settled;
    private bool cancellationObserved;
    private bool handlerOutcomeReturned;
    private bool handlerOutcomeSucceeded;
    private int settlementCount;

    public NativeCqrsObservationGrain()
    {
    }

    public Task RecordAsync(string eventName)
    {
        events.Add(eventName);
        return Task.CompletedTask;
    }

    public Task RecordObservationAsync(NativeCqrsLeafObservation observation)
    {
        leafObservation = observation;
        return Task.CompletedTask;
    }

    public Task RecordOutcomeAsync(bool succeeded)
    {
        handlerOutcomeReturned = true;
        handlerOutcomeSucceeded = succeeded;
        return Task.CompletedTask;
    }

    public Task MarkSettledAsync(bool cancellationObserved)
    {
        settled = true;
        this.cancellationObserved = cancellationObserved;
        settlementCount++;
        events.Add(NativeCqrsProtocol.Settled);
        return Task.CompletedTask;
    }

    public Task<NativeCqrsObservationSnapshot> ReadAsync()
    {
        var snapshot = new NativeCqrsObservationSnapshot(
            this.GetPrimaryKey(), Array.AsReadOnly(events.ToArray()), settled, cancellationObserved, settlementCount,
            leafObservation, handlerOutcomeReturned, handlerOutcomeSucceeded);
        return Task.FromResult(snapshot);
    }
}
