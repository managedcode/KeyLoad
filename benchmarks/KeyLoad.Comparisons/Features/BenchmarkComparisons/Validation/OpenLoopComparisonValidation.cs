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
            || worker.NodeCount <= 0 || worker.NodeCount > policy.MaximumNodes || !IsSupportedScenario(worker.Scenario)
            || !target.Supports(worker.Scenario) || !ValidSource(worker.SourceRevision)
            || worker.RunId <= 0 || worker.Attempt <= 0 || worker.JobId <= 0
            || !Within(worker.Repository, OpenLoopEvidenceContract.MaximumRepositoryCharacters)
            || !Within(worker.Ref, OpenLoopEvidenceContract.MaximumRefCharacters)
            || !Within(worker.Workflow, OpenLoopEvidenceContract.MaximumWorkflowCharacters))
        {
            throw new ArgumentOutOfRangeException(nameof(rate), OpenLoopFailureMessages.CellOutsideFrozenProfile);
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
        => value.Length == OpenLoopEvidenceContract.GitRevisionHexCharacters && value.All(Uri.IsHexDigit);

    private static bool Within(string value, int limit) => !string.IsNullOrWhiteSpace(value) && value.Length <= limit;
}
