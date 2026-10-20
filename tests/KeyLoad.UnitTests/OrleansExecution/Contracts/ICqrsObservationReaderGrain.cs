using ManagedCode.Orleans.Graph.Attributes;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

[AllowClientCall]
[global::Orleans.CodeGeneration.Version(1)]
internal interface ICqrsObservationReaderGrain : global::Orleans.IGrainWithGuidKey
{
    Task<CqrsObservationSnapshot> ReadAsync();
}
