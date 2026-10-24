using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Messaging;

internal sealed record QueueDeliveryVersionExhaustionState(QueueClaimVersionExhaustionState Data,
    Command<DatabaseCommand> Claim, Result<object> Claimed, Delivery Delivery, MessageMetadata RepairedMetadata);
