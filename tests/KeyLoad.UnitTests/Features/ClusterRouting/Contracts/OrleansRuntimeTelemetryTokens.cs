namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class OrleansRuntimeTelemetryTokens
{
    internal const string ApplicationSource = "Microsoft.Orleans.Application";
    internal const string LifecycleSource = "Microsoft.Orleans.Lifecycle";
    internal const string ParentSource = "KeyLoad.UnitTests.OrleansRuntimeTelemetry";
    internal const string ParentOperation = "telemetry-parent";
    internal const string SentinelEvent = "private-telemetry-sentinel";
    internal const string SentinelTag = "private.payload";
    internal const string SentinelValue = "telemetry-private-canary-value";
    internal const string SubjectPrefix = "telemetry-subject-";
    internal const string Collection = "orleans-telemetry-documents";
    internal const string DocumentJson = "{\"telemetry\":true}";
    internal const string DocumentPrefix = "telemetry-document-";
    internal const string MetricMeter = "Microsoft.Orleans";
    internal const string PrivacyMeter = "KeyLoad.ClusterRouting.TelemetryPrivacy";
    internal const string SafeSuppressionReason = "unsafe_event";
    internal const string SuppressionCounter = "keyload.orleans.suppressed_spans";
    internal const string SuppressionReasonTag = "reason";
    internal const string ApplicationDisplayName = "orleans.application.operation";
    internal const string LifecycleDisplayName = "orleans.lifecycle.operation";
    internal const string RequestMethodValue = "request.execute";
    internal const int ExportIntervalMilliseconds = 100;
    internal const int MaximumActivityTags = 32;
    internal const int MaximumActivityEvents = 24;
    internal const int FailedTerminalSequence = 2;
}
