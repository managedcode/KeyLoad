using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueDeliveryVersionExhaustionState(QueueClaimVersionExhaustionState Data,
    Command<DatabaseCommand> Claim, Result<object> Claimed, Delivery Delivery, MessageMetadata RepairedMetadata);
