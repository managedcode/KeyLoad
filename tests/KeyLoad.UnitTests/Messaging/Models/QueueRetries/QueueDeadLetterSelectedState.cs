namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueDeadLetterSelectedSaved(ReplicatedOperation Operation, OperationResult Result);
internal sealed record QueueDeadLetterSelectedRepair(byte[] Key, byte[] Bytes);
internal sealed record QueueDeadLetterSelectedState(QueueLaneRef Lane, DateTimeOffset Time,
    QueueDeadLetterSelectedOperation Kind, QueueDeadLetterSelectedFault Fault, string Target,
    ReplicatedOperation Original, QueueDeadLetterSelectedSaved[] History,
    QueueDeadLetterSelectedRepair[] Repairs, MessageBody FirstBody, MessageBody SecondBody);
