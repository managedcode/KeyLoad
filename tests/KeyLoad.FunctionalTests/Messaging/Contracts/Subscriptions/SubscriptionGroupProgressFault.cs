namespace KeyLoad.FunctionalTests.Messaging;

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
