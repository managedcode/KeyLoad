namespace KeyLoad.FunctionalTests.Messaging;

internal enum SubscriptionDeliveryAuthorityFault
{
    ForeignPosition,
    MissingLeaseExpiry,
    UndefinedState,
    MissingWindow
}
