namespace KeyLoad.Core.Features.Messaging;

/// <summary>Carries the original queue identity and bounded deadline mutation.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(QueueDeadlineHintContract.Alias)]
public sealed record QueueDeadlineHint(
    [property: global::Orleans.Id(QueueDeadlineHintContract.LaneField)] QueueLaneRef Lane,
    [property: global::Orleans.Id(QueueDeadlineHintContract.MutationField)] AdvanceQueueDeadline Mutation);
