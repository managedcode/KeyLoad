namespace KeyLoad.Core.Features.Messaging;

internal static class QueueDeadlineHintContract
{
    internal const string Alias = "keyload.core.messaging.queue-deadline-hint.v1";
    internal const int LaneField = 0;
    internal const int MutationField = 1;
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(QueueDeadlineHintContract.Alias)]
internal sealed record QueueDeadlineHint(
    [property: global::Orleans.Id(QueueDeadlineHintContract.LaneField)] QueueLaneRef Lane,
    [property: global::Orleans.Id(QueueDeadlineHintContract.MutationField)] AdvanceQueueDeadline Mutation);
