using ManagedCode.Communication.CQRS;
using ManagedCode.Orleans.Graph.Attributes;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

[AllowClientCall]
[AllowGrainCall(typeof(ICqrsLeafGrain), AllowAllMethods = false,
    SourceMethods = new string[] { nameof(AllowedAsync) },
    TargetMethods = new string[] { nameof(ICqrsLeafGrain.ObserveAsync) })]
[AllowGrainCall(typeof(ICqrsObservationSinkGrain), AllowAllMethods = true)]
[global::Orleans.CodeGeneration.Version(1)]
internal interface ICqrsStreamGrain : global::Orleans.IGrainWithGuidKey
{
    IAsyncEnumerable<CqrsStreamChunk<CqrsProgress, CqrsResult>> AllowedAsync(
        Guid requestId, CancellationToken cancellationToken);

    IAsyncEnumerable<CqrsStreamChunk<CqrsProgress, CqrsResult>> DeniedAsync(
        Guid requestId, CancellationToken cancellationToken);

    IAsyncEnumerable<CqrsStreamChunk<CqrsProgress, CqrsResult>> BackpressureAsync(
        Guid requestId, CancellationToken cancellationToken);

    IAsyncEnumerable<CqrsStreamChunk<CqrsProgress, CqrsResult>> FailAfterProgressAsync(
        Guid requestId, CancellationToken cancellationToken);
}
