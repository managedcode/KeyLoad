using ManagedCode.Communication.CQRS;
using ManagedCode.Orleans.Graph.Attributes;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

[AllowClientCall]
[AllowGrainCall(typeof(INativeCqrsLeafGrain), AllowAllMethods = false,
    SourceMethods = new string[] { nameof(AllowedAsync) },
    TargetMethods = new string[] { nameof(INativeCqrsLeafGrain.ObserveAsync) })]
[AllowGrainCall(typeof(INativeCqrsObservationSinkGrain), AllowAllMethods = true)]
internal interface INativeCqrsStreamGrain : global::Orleans.IGrainWithGuidKey
{
    IAsyncEnumerable<CqrsStreamChunk<NativeCqrsProgress, NativeCqrsResult>> AllowedAsync(
        Guid requestId, CancellationToken cancellationToken);

    IAsyncEnumerable<CqrsStreamChunk<NativeCqrsProgress, NativeCqrsResult>> DeniedAsync(
        Guid requestId, CancellationToken cancellationToken);

    IAsyncEnumerable<CqrsStreamChunk<NativeCqrsProgress, NativeCqrsResult>> BackpressureAsync(
        Guid requestId, CancellationToken cancellationToken);

    IAsyncEnumerable<CqrsStreamChunk<NativeCqrsProgress, NativeCqrsResult>> FailAfterProgressAsync(
        Guid requestId, CancellationToken cancellationToken);
}
