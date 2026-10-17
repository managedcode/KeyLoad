namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class CompositionSqlColdProtocol
{
    internal const string Message = "queued-link";
    internal const string EdgePrefix = "knowledge-";
    internal const string MessagePrefix = "next-";
    internal const string OtherGraph = "restored-links";
    internal const string Marker = "current-grant-marker";
    internal const string HealthyPrefix = "restored-";
    internal const int FirstEpoch = 1;
    internal const int NextEpoch = 2;
    internal const int HealthyEffects = 3;
    internal const int HealthyEdges = 2;
}
