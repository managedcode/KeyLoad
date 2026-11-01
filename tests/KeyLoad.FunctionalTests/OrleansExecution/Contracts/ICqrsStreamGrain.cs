using ManagedCode.Communication.CQRS;

namespace KeyLoad.FunctionalTests.OrleansExecution;

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
