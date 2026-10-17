namespace KeyLoad.UnitTests.Features.Messaging;

internal enum QueueLeaseAuthorityOperation
{
    Ack,
    Nack,
    Renew,
    Processing
}
