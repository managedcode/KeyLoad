namespace KeyLoad.Core.Features.Messaging;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(QueueDeadlineHintContract.Alias)]
internal sealed record QueueDeadlineHint(
    [property: global::Orleans.Id(QueueDeadlineHintContract.LaneField)] QueueLaneRef Lane,
    [property: global::Orleans.Id(QueueDeadlineHintContract.MutationField)] AdvanceQueueDeadline Mutation);
