namespace KeyLoad.Orleans;

/// <summary>Native-local evidence from the original fixed-voter cohort predicate; never authority.</summary>
/// <param name="Code">Closed branch actually evaluated.</param>
/// <param name="RequiredMajority">Original configured majority, or zero when not evaluated.</param>
/// <param name="EvaluatedRemote">Original non-local cache lookups evaluated before return.</param>
/// <param name="FreshRemote">Evaluated entries admitted by the original freshness bound.</param>
/// <param name="MissingRemote">Evaluated cache lookups with no entry.</param>
/// <param name="ExpiredRemote">Evaluated entries removed by the original expiry predicate.</param>
/// <param name="Ready">Compatible ready entries including the original local entry.</param>
public readonly record struct ReplicaCohortAdmissionStatus(ReplicaCohortAdmissionCode Code,
    int RequiredMajority, int EvaluatedRemote, int FreshRemote, int MissingRemote, int ExpiredRemote, int Ready)
{
    private const int NotEvaluated = 0;
    /// <summary>Whether this actual evaluation proved the unchanged native cohort predicate.</summary>
    public bool Compatible => Code == ReplicaCohortAdmissionCode.Compatible;

    internal static ReplicaCohortAdmissionStatus Unavailable(ReplicaCohortAdmissionCode code)
        => new(code, NotEvaluated, NotEvaluated, NotEvaluated, NotEvaluated, NotEvaluated, NotEvaluated);
}
