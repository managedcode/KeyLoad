namespace KeyLoad.UnitTests.Features.Messaging;

internal enum SubscriptionDeliveryAuthorityFault
{
    ForeignPosition,
    MissingLeaseExpiry,
    UndefinedState,
    MissingWindow
}
