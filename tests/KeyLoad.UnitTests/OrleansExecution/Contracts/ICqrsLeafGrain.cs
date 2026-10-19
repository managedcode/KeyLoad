namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal interface ICqrsLeafGrain : global::Orleans.IGrainWithGuidKey
{
    Task<CqrsLeafObservation> ObserveAsync(Guid requestId);
}
