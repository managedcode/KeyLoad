using KeyLoad.Query.Search;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class AnnProviderQualificationState(DistanceMetric metric)
{
    internal DistanceMetric Metric { get; } = metric;
    internal bool Deleted
    {
        get; set;
    }
    internal bool Reinserted
    {
        get; set;
    }
    internal bool Healthy
    {
        get; set;
    }
    internal List<AnnProviderQualificationHistory> History { get; } = [];
    internal byte[] OriginalFile { get; set; } = [];
    internal byte[] OriginalDigest { get; set; } = [];
    internal byte[] HealthyFile { get; set; } = [];
    internal byte[] HealthyDigest { get; set; } = [];
    internal byte[] DamagedFile { get; set; } = [];
    internal byte[] DamagedDigest { get; set; } = [];
    internal PackedAnnOptions Policy
    {
        get;
    } = new()
    {
        ExactThreshold = AnnProviderQualificationProtocol.GraphThreshold
    };
}
