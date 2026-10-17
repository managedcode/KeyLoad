using System.Collections.Immutable;

namespace KeyLoad;

internal static class InboxProcessingContractNames
{
    internal const string Request = "keyload.contract.commit-inbox-request.v1";
    internal const string Result = "keyload.contract.commit-inbox-result.v1";
    internal const string Policy = "keyload.contract.inbox-policy.v1";
}

/// <summary>Explicitly bounds lifetime retained target-inbox receipts without a pruning default.</summary>
/// <param name="MaxReceipts">The positive configured maximum receipt count.</param>
/// <param name="MaxBytes">The positive configured maximum retained key and record bytes.</param>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(InboxProcessingContractNames.Policy)]
public sealed record InboxPolicy(
    [property: global::Orleans.Id(0)] long MaxReceipts,
    [property: global::Orleans.Id(1)] long MaxBytes);

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

/// <summary>Returns the durable target receipt without asserting source acknowledgement.</summary>
/// <param name="Receipt">The original native effects receipt.</param>
/// <param name="AlreadyProcessed">Whether an existing inbox identity supplied the receipt.</param>
/// <param name="OriginalEffectsToken">The original durable target effects cut.</param>
[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(InboxProcessingContractNames.Result)]
public sealed record CommitInboxResult(
    [property: global::Orleans.Id(0)] CommitReceipt Receipt,
    [property: global::Orleans.Id(1)] bool AlreadyProcessed,
    [property: global::Orleans.Id(2)] CommitToken OriginalEffectsToken);
