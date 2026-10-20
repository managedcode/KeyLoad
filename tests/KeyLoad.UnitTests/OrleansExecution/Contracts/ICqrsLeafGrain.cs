namespace KeyLoad.UnitTests.Features.OrleansExecution;

[global::Orleans.CodeGeneration.Version(1)]
internal interface ICqrsLeafGrain : global::Orleans.IGrainWithGuidKey
{
    Task<CqrsLeafObservation> ObserveAsync(Guid requestId);
}
