namespace KeyLoad.UnitTests.OrleansExecution;

internal readonly record struct OrleansMetricPointCapture(string MeterName, string MetricName,
    KeyValuePair<string, string?>[] Tags, KeyValuePair<string, string?>[] ExemplarTags);
