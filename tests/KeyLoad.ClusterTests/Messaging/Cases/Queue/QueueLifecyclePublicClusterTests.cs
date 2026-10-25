using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey), NotInParallel]
internal sealed class QueueLifecyclePublicClusterTests(ClusterFixture fixture)
{
    [Test, Arguments(0), Arguments(1), Arguments(2), Arguments(3)]
    public Task ParkedPendingAtomicRefusalCancelRedriveFreshLeaseFourRoutesAndSameOwnerCold(int route)
        => QueueLifecyclePublicTrial.RunAsync(fixture, route);

    [Test, Arguments(false), Arguments(true)]
    public Task CurrentLeasedCancellationRollsBackLateFailureThenFencesOldTokensTwoColdAndSameLaneHealthy(bool mcpFirst)
        => QueueLeasedCancelPublicFlow.RunAsync(fixture, mcpFirst);

    [Test, Arguments(false), Arguments(true)]
    public Task ByteOnlyDeadLetterSublimitRetainsPendingThroughAtomicRefusalTwoColdRedriveAndSameLaneHealthy(bool mcpFirst)
        => QueueByteSublimitPublicFlow.RunAsync(fixture, mcpFirst);

    [Test, Arguments(false), Arguments(true)]
    public Task ScheduledCancellationRollsBackLateFailureThenRejectsOriginalDeadlineCasAcrossBothColdCutsAndHealthyWork(bool mcpFirst)
        => QueueScheduledCancelPublicFlow.RunAsync(fixture, mcpFirst);

    [Test, Arguments(false), Arguments(true)]
    public Task HistoricalEmptyReceiveCannotClaimLaterReadyWorkChangedSameIdRefusesAcrossBothColdCutsAndHealthyWork(bool mcpFirst)
        => QueueEmptyHistoryPublicFlow.RunAsync(fixture, mcpFirst);
}
