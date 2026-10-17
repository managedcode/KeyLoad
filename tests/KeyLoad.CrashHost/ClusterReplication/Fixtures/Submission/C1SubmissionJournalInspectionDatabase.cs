using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.ClusterReplication;

internal sealed class C1SubmissionJournalInspectionDatabase : IDisposable
{
    private readonly C1SubmissionJournalInspectionStore borrowed;

    private C1SubmissionJournalInspectionDatabase(C1SubmissionJournalInspectionStore borrowed, DatabaseEngine database)
    {
        this.borrowed = borrowed;
        Database = database;
    }

    internal DatabaseEngine Database
    {
        get;
    }

    internal static C1SubmissionJournalInspectionDatabase Open(ZoneTreeStore canonical,
        C1SubmissionJournalInspectionReadBudget budget, CancellationToken cancellationToken)
    {
        var borrowed = new C1SubmissionJournalInspectionStore(canonical, budget, cancellationToken);
        try
        {
            var database = new DatabaseEngine(borrowed, new AuthorizationPolicy(), CrashExecutionOptions.DatabaseLimits(),
                CrashExecutionOptions.DueWork(), CrashExecutionOptions.EventSource(), CrashExecutionOptions.Messaging(),
                CrashExecutionOptions.GraphExecution(), CrashExecutionOptions.ChangeFeedExecution(), CrashExecutionOptions.BlobExecution(),
                CrashExecutionOptions.NativeClaimsExecution(), CrashExecutionOptions.TimeSeriesExecution(),
                CrashExecutionOptions.MovementCheckpoints(), UnavailablePartitionMovementCheckpointVerifier.Instance);
            return new(borrowed, database);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            global::KeyLoad.Server.ServerFailureObserver.Observe(borrowed.Dispose, failures);
            global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    public void Dispose() => borrowed.Dispose();
}
