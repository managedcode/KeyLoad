namespace KeyLoad.Comparisons;

internal static class OpenLoopComparisonValidation
{
    internal static void Validate(ScaledComparisonProfile profile, int rate, IComparisonTarget target,
        IsolatedComparisonWorker worker, OpenLoopExecutionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(worker);
        ArgumentNullException.ThrowIfNull(policy);
        if (!policy.IsQualifiedV1()) throw Invalid();
        if (!OperatingSystem.IsLinux() || !OpenLoopRateContract.AcceptedRates.Contains(rate)
            || worker.Profile != profile.Id || worker.Target != target.Profile.Name
            || profile.Operations != OpenLoopRateContract.PlannedOperations
            || profile.Concurrency != policy.ConcurrentSessions
            || worker.NodeCount is < 1 or > policy.MaximumNodes || !IsSupportedScenario(worker.Scenario)
            || !target.Supports(worker.Scenario) || !ValidSource(worker.SourceRevision)
            || worker.RunId < 1 || worker.Attempt < 1 || worker.JobId < 1
            || !Within(worker.Repository, 512) || !Within(worker.Ref, 512) || !Within(worker.Workflow, 256))
        {
            throw new ArgumentOutOfRangeException(nameof(rate), "The open-loop cell is outside its frozen profile.");
        }
    }

    internal static void ValidateObservedTarget(IsolatedComparisonWorker worker, TargetProfile profile)
    {
        if (profile.Cluster is not { } cluster || cluster.Nodes != worker.NodeCount)
        {
            throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopObservedTopologyMismatch);
        }
    }

    private static bool IsSupportedScenario(Scenario scenario)
        => scenario is Scenario.PointRead or Scenario.DocumentWrite or Scenario.DocumentUpdate or Scenario.DocumentDelete;

    private static bool ValidSource(string value)
        => value.Length == 40 && value.All(Uri.IsHexDigit);

    private static bool Within(string value, int limit) => !string.IsNullOrWhiteSpace(value) && value.Length <= limit;
}
