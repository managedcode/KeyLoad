namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class DistributedQueryAuthorityProtocol
{
    internal const long InitialSchema = 1L;
    internal const long ChangedSchema = 2L;
    internal const long RepairedSchema = 3L;
    internal const long VersionStep = 1L;
    internal const long InitialEpoch = 2L;
    internal const long DeniedEpoch = 3L;
    internal const long RepairedEpoch = 4L;
    internal const long FirstRevision = 1L;
    internal const long EmptyPosition = 0L;
    internal const int LeafCount = 2;
    internal const int DestinationLeaf = 0;
    internal const int SourceLeaf = 1;
    internal const int FirstVoter = 0;
    internal const int FirstCold = 1;
    internal const int LastCold = 2;
    internal const int ColdStep = 1;
}
