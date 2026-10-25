using ManagedCode.Orleans.Graph.Attributes;

namespace KeyLoad.FunctionalTests.OrleansExecution;

[AllowClientCall]
[global::Orleans.CodeGeneration.Version(1)]
internal interface ICqrsObservationReaderGrain : global::Orleans.IGrainWithGuidKey
{
    Task<CqrsObservationSnapshot> ReadAsync();
}
