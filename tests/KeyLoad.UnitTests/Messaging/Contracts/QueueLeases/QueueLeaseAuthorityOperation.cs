namespace KeyLoad.UnitTests.Messaging;

internal enum QueueLeaseAuthorityOperation
{
    Ack,
    Nack,
    Renew,
    Processing
}
