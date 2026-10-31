namespace KeyLoad.Clustering;

internal static class GrainRequestStreamProtocol
{
    internal const int MaximumChunks = 2;
    internal const int BatchSize = 1;
    internal const string ContextKey = "KeyLoad-RequestState";
    internal const string AuthenticationType = "KeyLoad.PersistedPrincipal";
}
