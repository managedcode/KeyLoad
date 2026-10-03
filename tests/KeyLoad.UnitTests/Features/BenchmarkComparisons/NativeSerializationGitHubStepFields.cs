namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Names used only by controlled production step-classification inputs and responses.</summary>
internal static class NativeSerializationGitHubStepFields
{
    internal const string Job = "job";
    internal const string Steps = "steps";
    internal const string Name = "name";
    internal const string Status = "status";
    internal const string Conclusion = "conclusion";
    internal const string Measured = "measured";
    internal const string Mode = "mode";
    internal const string Start = "start";
    internal const string Now = "now";
    internal const string Nonfinite = "nonfinite";
    internal const string Accepted = "accepted";
    internal const string Ready = "ready";
    internal const string Completed = "completed";
    internal const string Unchanged = "unchanged";
    internal const string Error = "error";
    internal const string StrictError = "strictError";
    internal const string Wait = "wait";
    internal const string StepsError = "Invalid native-serialization evidence: github.steps.";
    internal const string RequiredStepError = "Invalid native-serialization evidence: github.requiredStep.";
    internal const string ClockError = "Invalid native-serialization evidence: github.visibilityClock.";
    internal const string DeadlineError = "Invalid native-serialization evidence: github.visibilityDeadline.";
    internal const string Build = "Build diagnostic tests and generated benchmark host";
    internal const string Normal = "Test native diagnostic contracts and generated consumers";
    internal const string Scalar = "Test diagnostic contracts without hardware intrinsics";
    internal const string Record = "Record source packages runtime and execution identity";
    internal const string Measure = "Measure all native and historical JSON diagnostic cases";
}
