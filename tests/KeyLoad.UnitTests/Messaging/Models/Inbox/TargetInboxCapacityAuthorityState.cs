using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record TargetInboxCapacityAuthorityState(CommitInboxRequest Original, CommitInboxRequest Second,
    CommitInboxRequest Healthy, byte[] Capacity, byte[] OriginalRecord, byte[] SecondRecord,
    Result<object> OriginalResult, Result<object> SecondResult, DeliveryCommand SourceAck, Result<object> SourceAckResult, EntityRef HealthyReference);
