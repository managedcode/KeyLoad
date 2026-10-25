namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class ConnectionIdentityCollisionProtocol
{
    internal const string SecondDocument = "execution-identity-collision-second";
    internal const string SecondJson = "{\"identity\":\"second\",\"value\":2}";
    internal const string CapacityDetail = "Execution request work capacity is exhausted.";
    internal const string Failure = "The original execution connection identity contract was not observed.";
    internal const int Empty = 0;
    internal const int One = 1;
    internal const int Two = 2;
    internal const int OriginalOrdinal = 0;
    internal const long InitialRevision = 1;
}
