namespace KeyLoad.UnitTests.Messaging;

internal enum SubscriptionDeliveryAuthorityFault
{
    ForeignPosition,
    MissingLeaseExpiry,
    UndefinedState,
    MissingWindow
}
