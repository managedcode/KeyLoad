namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ConnectionCallerFactoryProtocol
{
    internal const string Duplicate = "The native operation was observed more than once.";
    internal const string Missing = "The native connection operation has no observation.";
    internal const string Failure = "The original native caller factory contract was not observed.";
    internal const string Unjoined = "The original borrowed native caller task has not joined.";
    internal const int NoInvocations = 0;
}
