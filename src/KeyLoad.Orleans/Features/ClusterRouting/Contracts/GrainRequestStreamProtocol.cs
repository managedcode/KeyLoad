namespace KeyLoad.Orleans;

internal static class GrainRequestStreamProtocol
{
    internal const int MaximumChunks = 2;
    internal const int BatchSize = 1;
    internal const int MaximumStartedBytes = 8_192;
    internal const int MaximumFailedBytes = 65_536;
    internal const int MaximumCompletedBytes = 16_842_752;
    internal const int MaximumAggregateBytes = 16_850_944;
    internal const int MaximumScratchBytes = 1_048_576;
    internal const int MaximumDetailCharacters = 4_096;
    internal const int MaximumPrincipalBytes = 4_096;
    internal const int MaximumContextBytes = 256;
    internal const string ContextKey = "KeyLoad-RequestState";
    internal const string AuthenticationType = "KeyLoad.PersistedPrincipal.v1";
}
