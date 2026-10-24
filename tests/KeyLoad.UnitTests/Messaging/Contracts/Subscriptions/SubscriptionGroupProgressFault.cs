namespace KeyLoad.UnitTests.Messaging;

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
