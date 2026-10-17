using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Commits authorized target effects and application-declared dedup identity without acknowledging a source.</summary>
/// <param name="CommandId">The original stable command identity.</param>
/// <param name="Target">The configured target inbox namespace and atomic partition.</param>
/// <param name="Source">The declared source identity, which conveys no source authority.</param>
/// <param name="MessageId">The canonical application input identity.</param>
/// <param name="DeliveryGeneration">The explicit input lifetime.</param>
/// <param name="HandlerScope">The canonical handler scope.</param>
/// <param name="ExecutionGeneration">The explicit handler/redrive lifetime.</param>
/// <param name="Effects">The bounded authorized target-partition mutations.</param>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(InboxProcessingContractNames.Request)]
public sealed record CommitInboxRequest(
    [property: global::Orleans.Id(0)] Guid CommandId,
    [property: global::Orleans.Id(1)] QueueLaneRef Target,
    [property: global::Orleans.Id(2)] QueueLaneRef Source,
    [property: global::Orleans.Id(3)] string MessageId,
    [property: global::Orleans.Id(4)] long DeliveryGeneration,
    [property: global::Orleans.Id(5)] string HandlerScope,
    [property: global::Orleans.Id(6)] long ExecutionGeneration,
    [property: global::Orleans.Id(7)] ImmutableArray<Mutation> Effects);
