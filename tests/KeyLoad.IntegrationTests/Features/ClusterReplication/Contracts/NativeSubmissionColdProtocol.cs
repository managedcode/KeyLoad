namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class NativeSubmissionColdProtocol
{
    internal const int ColdOwners = 2;
    internal const int FirstCold = 0;
    internal const int PlacementVersion = 1;
    internal const long OriginalCurrentRevision = 3;
    internal const long HealthyRevision = 1;
    internal const long AbsentRevision = 0;
    internal const string HealthyDocument = "native-failed-ack-cold-healthy";
    internal const string HealthyJson = "{\"coldHealthy\":true}";
    internal const string PutKind = "putDocument";
}
