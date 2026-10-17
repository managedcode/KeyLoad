using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Search;

/// <summary>AC-FTS-INCREMENTAL-005: actual original pending intent corruption, repair and immutable healthy continuation.</summary>
internal sealed class NativeTextIncrementalIntentCorruptionTests
{
    [Test]
    public async Task DamagedDurableOriginalIntentRejectsThenExactRepairReplaysOriginalCheckpointAndLiteralColdPages()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var database = new TestDatabase(nativeReplicaAdmission: true);
            await ServerFailureObserver.ObserveAsync(() => NativeTextIncrementalIntentCorruptionTrial.RunAsync(database,
                TestContext.Current!.Execution.CancellationToken), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
