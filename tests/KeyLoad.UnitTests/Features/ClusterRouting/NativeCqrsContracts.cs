using ManagedCode.Communication.CQRS;
using ManagedCode.Orleans.Graph.Attributes;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeCqrsProtocol
{
    internal const int BatchSize = 1;
    internal const int ExpectedCompletedChunks = 3;
    internal const int ExpectedDeniedChunks = 2;
    internal const int ExpectedFailureChunks = 3;
    internal const int ExpectedSettlementCount = 1;
    internal const long FirstSequence = 1;
    internal const string ProgressAlias = "KeyLoad.Tests.NativeCqrsProgress.v1";
    internal const string LeafObservationAlias = "KeyLoad.Tests.NativeCqrsLeafObservation.v1";
    internal const string ResultAlias = "KeyLoad.Tests.NativeCqrsResult.v1";
    internal const string SnapshotAlias = "KeyLoad.Tests.NativeCqrsSnapshot.v1";
    internal const string Empty = "";
    internal const string AllowedGuid = "81000000-0000-0000-0000-000000000001";
    internal const string DeniedGuid = "81000000-0000-0000-0000-000000000002";
    internal const string ConcurrentOneGuid = "81000000-0000-0000-0000-000000000003";
    internal const string ConcurrentTwoGuid = "81000000-0000-0000-0000-000000000004";
    internal const string CancellationGuid = "81000000-0000-0000-0000-000000000005";
    internal const string DisposalGuid = "81000000-0000-0000-0000-000000000006";
    internal const string HealthyAfterCancellationGuid = "81000000-0000-0000-0000-000000000007";
    internal const string HealthyAfterDisposalGuid = "81000000-0000-0000-0000-000000000008";
    internal const string FailureGuid = "81000000-0000-0000-0000-000000000009";
    internal const string Started = "started";
    internal const string ProgressOne = "progress-one-written";
    internal const string ProgressTwoAttempted = "progress-two-attempted";
    internal const string ProgressTwo = "progress-two-written";
    internal const string LeafCallAttempted = "leaf-call-attempted";
    internal const string Settled = "settled";
    internal const string GraphDenialExceptionTitle = nameof(InvalidOperationException);
    internal const int GraphDenialStatus = 500;
    internal const string FailureTitle = "operation-rejected";
    internal const string FailureDetail = "The operation was rejected after progress.";
    internal const int FailureStatus = 422;
}

internal static class NativeCqrsRequestIds
{
    internal static readonly Guid Allowed = Guid.Parse(NativeCqrsProtocol.AllowedGuid);
    internal static readonly Guid Denied = Guid.Parse(NativeCqrsProtocol.DeniedGuid);
    internal static readonly Guid ConcurrentOne = Guid.Parse(NativeCqrsProtocol.ConcurrentOneGuid);
    internal static readonly Guid ConcurrentTwo = Guid.Parse(NativeCqrsProtocol.ConcurrentTwoGuid);
    internal static readonly Guid Cancellation = Guid.Parse(NativeCqrsProtocol.CancellationGuid);
    internal static readonly Guid Disposal = Guid.Parse(NativeCqrsProtocol.DisposalGuid);
    internal static readonly Guid HealthyAfterCancellation = Guid.Parse(NativeCqrsProtocol.HealthyAfterCancellationGuid);
    internal static readonly Guid HealthyAfterDisposal = Guid.Parse(NativeCqrsProtocol.HealthyAfterDisposalGuid);
    internal static readonly Guid Failure = Guid.Parse(NativeCqrsProtocol.FailureGuid);
}

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeCqrsProtocol.ProgressAlias)]
internal sealed record NativeCqrsProgress(
    [property: global::Orleans.Id(0)] Guid RequestId,
    [property: global::Orleans.Id(1)] string Stage);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeCqrsProtocol.LeafObservationAlias)]
internal sealed record NativeCqrsLeafObservation(
    [property: global::Orleans.Id(0)] Guid RequestId,
    [property: global::Orleans.Id(1)] string CallerContext,
    [property: global::Orleans.Id(2)] string TargetInterface,
    [property: global::Orleans.Id(3)] string TargetMethod,
    [property: global::Orleans.Id(4)] string ObservedSource,
    [property: global::Orleans.Id(5)] string ObservedTarget,
    [property: global::Orleans.Id(6)] string ObservedSourceMethod,
    [property: global::Orleans.Id(7)] string ObservedTargetMethod,
    [property: global::Orleans.Id(8)] string OutgoingSourceId,
    [property: global::Orleans.Id(9)] string OutgoingTargetId,
    [property: global::Orleans.Id(10)] string IncomingSourceId,
    [property: global::Orleans.Id(11)] string IncomingTargetId,
    [property: global::Orleans.Id(12)] string LeafPrimaryKey);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeCqrsProtocol.ResultAlias)]
internal sealed record NativeCqrsResult(
    [property: global::Orleans.Id(0)] Guid RequestId,
    [property: global::Orleans.Id(1)] NativeCqrsLeafObservation? Observation);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeCqrsProtocol.SnapshotAlias)]
internal sealed record NativeCqrsObservationSnapshot(
    [property: global::Orleans.Id(0)] Guid RequestId,
    [property: global::Orleans.Id(1)] IReadOnlyList<string> Events,
    [property: global::Orleans.Id(2)] bool Settled,
    [property: global::Orleans.Id(3)] bool CancellationObserved,
    [property: global::Orleans.Id(4)] int SettlementCount,
    [property: global::Orleans.Id(5)] NativeCqrsLeafObservation? Observation,
    [property: global::Orleans.Id(6)] bool HandlerOutcomeReturned,
    [property: global::Orleans.Id(7)] bool HandlerOutcomeSucceeded);

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

internal interface INativeCqrsLeafGrain : global::Orleans.IGrainWithGuidKey
{
    Task<NativeCqrsLeafObservation> ObserveAsync(Guid requestId);
}

internal interface INativeCqrsObservationSinkGrain : global::Orleans.IGrainWithGuidKey
{
    Task RecordAsync(string eventName);
    Task RecordObservationAsync(NativeCqrsLeafObservation observation);
    Task RecordOutcomeAsync(bool succeeded);
    Task MarkSettledAsync(bool cancellationObserved);
}

[AllowClientCall]
internal interface INativeCqrsObservationReaderGrain : global::Orleans.IGrainWithGuidKey
{
    Task<NativeCqrsObservationSnapshot> ReadAsync();
}
