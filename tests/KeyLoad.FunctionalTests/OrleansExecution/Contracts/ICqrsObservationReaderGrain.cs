
namespace KeyLoad.FunctionalTests.OrleansExecution;

internal interface ICqrsObservationReaderGrain : global::Orleans.IGrainWithGuidKey
{
    Task<CqrsObservationSnapshot> ReadAsync();
}
