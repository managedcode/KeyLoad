namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed record SerializationGitHubStepResponse(int ExitCode, string StandardError,
    bool Accepted, bool Ready, bool Completed, bool Unchanged, string Error, string StrictError, double? Wait);
