namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class CqrsObservationGrain : Grain,
    ICqrsObservationSinkGrain,
    ICqrsObservationReaderGrain
{
    private readonly List<string> events = [];
    private CqrsLeafObservation? leafObservation;
    private bool settled;
    private bool cancellationObserved;
    private bool handlerOutcomeReturned;
    private bool handlerOutcomeSucceeded;
    private int settlementCount;

    public CqrsObservationGrain()
    {
    }

    public Task RecordAsync(string eventName)
    {
        events.Add(eventName);
        return Task.CompletedTask;
    }

    public Task RecordObservationAsync(CqrsLeafObservation observation)
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
        events.Add(CqrsProtocol.Settled);
        return Task.CompletedTask;
    }

    public Task<CqrsObservationSnapshot> ReadAsync()
    {
        var snapshot = new CqrsObservationSnapshot(
            this.GetPrimaryKey(), Array.AsReadOnly(events.ToArray()), settled, cancellationObserved, settlementCount,
            leafObservation, handlerOutcomeReturned, handlerOutcomeSucceeded);
        return Task.FromResult(snapshot);
    }
}
