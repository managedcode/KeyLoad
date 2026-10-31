namespace KeyLoad.FunctionalTests.Messaging;

internal enum QueueWorkerInvalidPolicy
{
    HandlerLimit,
    ReceiveBudget,
    RenewalBudget,
    RenewalAtExpiry,
    EmptyPoll,
    UncertaintyCount,
    LeaseDuration
}
