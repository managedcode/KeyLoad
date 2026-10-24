namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGatePhysicalFileProtocol
{
    internal const string Canonical = "database";
    internal const string Tree = "tree";
    internal const string Identity = "identity.json";
    internal const string Failure = "The original functional silo physical file evidence is invalid.";
    internal const int MaximumEntries = 4096;
    internal const int MaximumPathCharacters = 4096;
    internal const int MaximumIdentityBytes = 65536;
    internal const int StreamBufferBytes = 4096;
    internal const int NoEntries = 0;
}
