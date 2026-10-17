namespace KeyLoad.UnitTests.Features.Messaging;

internal enum SubscriptionGroupProgressFault
{
    NegativeCheckpoint,
    NegativeIssuedPosition,
    SignedOverflow,
    IssuedBeforeExistingRow,
    FutureCheckpoint,
    IssuedBeforeCheckpoint,
    IssuedBeyondTail,
    WindowExceeded
}
