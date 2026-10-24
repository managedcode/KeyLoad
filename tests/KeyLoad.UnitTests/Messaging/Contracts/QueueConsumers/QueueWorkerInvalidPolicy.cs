namespace KeyLoad.UnitTests.Messaging;

internal enum QueueWorkerInvalidPolicy
{
    HandlerLimit,
    ReceiveBudget,
    RenewalBudget,
    ReceiveBytes,
    RenewalAtExpiry,
    EmptyPoll,
    UncertaintyCount,
    LeaseDuration
}
