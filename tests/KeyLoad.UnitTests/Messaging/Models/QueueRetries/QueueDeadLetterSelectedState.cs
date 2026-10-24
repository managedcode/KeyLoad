using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Messaging;

internal sealed record QueueDeadLetterSelectedState(QueueLaneRef Lane, DateTimeOffset Time,
    QueueDeadLetterSelectedOperation Kind, QueueDeadLetterSelectedFault Fault, string Target,
    Command<DatabaseCommand> Original, QueueDeadLetterSelectedSaved[] History,
    QueueDeadLetterSelectedRepair[] Repairs, MessageBody FirstBody, MessageBody SecondBody, EntityRefLifetime DocumentLifetime);
