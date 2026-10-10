using KeyLoad.Replication;

namespace KeyLoad.Server;

internal sealed partial class PhysicalShardCatalogPrerequisiteObservation
{
    private const int PrerequisiteFailureEvent = 1016;
    private const string PrerequisiteFailureMessage =
        "Catalog startup NativeReplicaPrerequisite failed: Cancelled={Cancelled} Code={Code} "
        + "TransportReady={TransportReady} LeaderPresent={LeaderPresent} Role={Role} Term={Term} "
        + "CommittedIndex={CommittedIndex} MaterializedPosition={MaterializedPosition} "
        + "CompatibleCohort={CompatibleCohort} LeaderConfirmed={LeaderConfirmed} "
        + "CohortCode={CohortCode} RequiredMajority={RequiredMajority} EvaluatedRemote={EvaluatedRemote} "
        + "FreshRemote={FreshRemote} MissingRemote={MissingRemote} ExpiredRemote={ExpiredRemote} Ready={Ready}.";
    private ReplicaNodeState? state;
    private bool? compatibleCohort;
    private KeyLoad.Orleans.ReplicaCohortAdmissionStatus cohortAdmission;
    private bool? leaderConfirmed;

    internal void Capture(ReplicaNodeState actual)
    {
        state = actual;
        compatibleCohort = null;
        cohortAdmission = default;
        leaderConfirmed = null;
    }

    internal bool Cohort(KeyLoad.Orleans.ReplicaCohortAdmissionStatus actual)
    { cohortAdmission = actual; compatibleCohort = actual.Compatible; return actual.Compatible; }

    internal bool Leader(bool actual)
    { leaderConfirmed = actual; return actual; }

    internal void Failed(ILogger logger, Exception original)
    {
        var failures = new List<Exception> { original };
        ServerFailureObserver.Observe(() => WriteFailure(logger, original is OperationCanceledException,
            original is KeyLoadException known ? known.Code : ErrorCode.UnknownWriteOutcome,
            state?.TransportReady, state is null ? null : state.LeaderId is not null, state?.Role,
            state?.Term, state?.CommittedIndex, state?.MaterializedPosition, compatibleCohort, leaderConfirmed,
            cohortAdmission.Code, cohortAdmission.RequiredMajority, cohortAdmission.EvaluatedRemote,
            cohortAdmission.FreshRemote, cohortAdmission.MissingRemote, cohortAdmission.ExpiredRemote,
            cohortAdmission.Ready), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    [LoggerMessage(EventId = PrerequisiteFailureEvent, Level = LogLevel.Warning, Message = PrerequisiteFailureMessage)]
    private static partial void WriteFailure(ILogger logger, bool cancelled, ErrorCode code,
        bool? transportReady, bool? leaderPresent, ReplicaRole? role, long? term, long? committedIndex,
        long? materializedPosition, bool? compatibleCohort, bool? leaderConfirmed,
        KeyLoad.Orleans.ReplicaCohortAdmissionCode cohortCode, int requiredMajority, int evaluatedRemote, int freshRemote,
        int missingRemote, int expiredRemote, int ready);
}

