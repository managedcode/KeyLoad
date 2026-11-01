
namespace KeyLoad.FunctionalTests.OrleansExecution;

[global::Orleans.CodeGeneration.Version(1)]
internal interface ICqrsObservationReaderGrain : global::Orleans.IGrainWithGuidKey
{
    Task<CqrsObservationSnapshot> ReadAsync();
}
