namespace KeyLoad.FunctionalTests.Messaging;

internal enum QueueLeaseAuthorityOperation
{
    Ack,
    Nack,
    Renew,
    Processing
}
