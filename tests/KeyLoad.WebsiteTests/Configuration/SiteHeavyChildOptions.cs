namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

[KeyLoad.ConfigurationOptions]
internal sealed class SiteHeavyChildOptions
{
    public int Capacity { get; set; }
    public int MaximumQueued { get; set; }
    public TimeSpan AdmissionDeadline { get; set; }
}
