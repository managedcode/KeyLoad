using ManagedCode.Orleans.Graph.Attributes;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

[AllowClientCall]
internal interface ICqrsObservationReaderGrain : global::Orleans.IGrainWithGuidKey
{
    Task<CqrsObservationSnapshot> ReadAsync();
}
