namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueDeadLetterSelectedState(QueueLaneRef Lane, DateTimeOffset Time,
    QueueDeadLetterSelectedOperation Kind, QueueDeadLetterSelectedFault Fault, string Target,
    ReplicatedOperation Original, QueueDeadLetterSelectedSaved[] History,
    QueueDeadLetterSelectedRepair[] Repairs, MessageBody FirstBody, MessageBody SecondBody);
