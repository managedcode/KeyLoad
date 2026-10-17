namespace KeyLoad.UnitTests.Features.Messaging;

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
