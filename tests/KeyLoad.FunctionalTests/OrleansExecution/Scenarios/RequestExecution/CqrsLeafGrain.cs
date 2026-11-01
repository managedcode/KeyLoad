namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class CqrsLeafGrain : Grain, ICqrsLeafGrain
{
    public Task<CqrsLeafObservation> ObserveAsync(Guid requestId)
        => Task.FromResult(new CqrsLeafObservation(requestId,
            typeof(ICqrsLeafGrain).FullName ?? CqrsProtocol.Empty,
            nameof(ICqrsLeafGrain.ObserveAsync),
            this.GetPrimaryKey().ToString()));
}
