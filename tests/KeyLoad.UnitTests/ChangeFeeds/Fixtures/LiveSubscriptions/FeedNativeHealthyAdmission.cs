namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal static class FeedNativeHealthyAdmission
{
    private const string MissingResult = "The joined native healthy admission must retain its actual operation outcome.";

    internal static global::KeyLoad.OperationResult Submit(global::KeyLoad.Core.DatabaseEngine owner,
        global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, string directory,
        global::KeyLoad.ReplicatedOperation operation, bool explicitTime, CancellationToken token)
    {
        var failures = new List<Exception>();
        global::KeyLoad.OperationResult? result = null;
        global::KeyLoad.Server.ServerFailureObserver.Observe(() =>
        {
            using var admission = new TestDatabaseReplicaAdmission(owner, store, directory,
                UnitExecutionOptions.ReplicaExecution());
            global::KeyLoad.Server.ServerFailureObserver.Observe(
                () => { result = admission.Submit(operation, explicitTime, token); }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(MissingResult);
    }
}
