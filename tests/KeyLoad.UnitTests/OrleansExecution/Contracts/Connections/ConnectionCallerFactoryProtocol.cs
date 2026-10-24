namespace KeyLoad.UnitTests.OrleansExecution;

internal static class ConnectionCallerFactoryProtocol
{
    internal const string Duplicate = "The execution operation was observed more than once.";
    internal const string Missing = "The execution connection operation has no observation.";
    internal const string Failure = "The original execution caller factory contract was not observed.";
    internal const string Unjoined = "The original borrowed execution caller task has not joined.";
    internal const int NoInvocations = 0;
}
