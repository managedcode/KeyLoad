namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal readonly record struct OrleansMetricPointCapture(string MeterName, string MetricName,
    KeyValuePair<string, string?>[] Tags, KeyValuePair<string, string?>[] ExemplarTags);
