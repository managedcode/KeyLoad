using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.TimeSeries;

internal static class TimeSeriesChunkQualificationScopeImage
{
    private const int MaximumRecords = 1000;
    private const string SampleFamily = "sample";
    private const string SampleIdentityFamily = "sample-id";
    private const string SampleSequenceFamily = "sample-sequence";
    private const string RetentionFamily = "sample-retention";
    private const string WindowFamily = "sample-chunk-window";
    private const string ManifestFamily = "sample-chunk-manifest";
    private const string BlockFamily = "sample-chunk-block";
    private const string CorrectionFamily = "sample-chunk-correction";
    private const string ImageSeparator = ":";
    private static readonly string[] Families =
        [SampleFamily, SampleIdentityFamily, SampleSequenceFamily, RetentionFamily, WindowFamily,
         ManifestFamily, BlockFamily, CorrectionFamily];
    internal static string[] Capture(ZoneTreeStore store, PartitionRef partition) => store.Read(view =>
    {
        var rows = new List<string>();
        foreach (var family in Families)
        {
            var scan = view.VisitRange(KeySpace.Partition(family, partition, TimeSeriesChunkQualificationData.Set,
                TimeSeriesChunkQualificationData.Series), MaximumRecords, (key, value) =>
            {
                rows.Add(Convert.ToHexString(key) + ImageSeparator + Convert.ToHexString(value));
                return true;
            });
            if (scan.HasMore)
            {
                throw new InvalidOperationException("The qualification scope image exceeds its frozen bound.");
            }
        }
        return rows.ToArray();
    });
}
