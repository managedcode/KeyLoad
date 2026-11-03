namespace KeyLoad.FunctionalTests.OrleansExecution;

internal interface ICqrsLeafGrain : global::Orleans.IGrainWithGuidKey
{
    Task<CqrsLeafObservation> ObserveAsync(Guid requestId);
}
