using KeyLoad.ProcessRecoveryTests.DocumentStorage;
using KeyLoad.ProcessRecoveryTests.Search;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.ProcessRecoveryTests;

internal static class CrashScenarioDispatcher
{
    internal static Task RunAsync(string directory, ZoneTreeStore store,
        CanonicalCrashBoundary boundary, string mode) => mode switch
        {
            global::KeyLoad.ProcessRecoveryTests.Messaging.RemoteTransferRepairCrashProtocol.AcceptMode
                => global::KeyLoad.ProcessRecoveryTests.Messaging.RemoteTransferRepairCrashScenario.RunAsync(directory, store, boundary, QueueTransferRepairStage.Accept),
            global::KeyLoad.ProcessRecoveryTests.Messaging.RemoteTransferRepairCrashProtocol.CompleteMode
                => global::KeyLoad.ProcessRecoveryTests.Messaging.RemoteTransferRepairCrashScenario.RunAsync(directory, store, boundary, QueueTransferRepairStage.Complete),
            global::KeyLoad.ProcessRecoveryTests.Messaging.RemoteTransferAttemptCrashProtocol.Mode
                => global::KeyLoad.ProcessRecoveryTests.Messaging.RemoteTransferAttemptCrashScenario.RunAsync(directory, store, boundary),
            global::KeyLoad.ProcessRecoveryTests.Messaging.TargetInboxCrashProtocol.Mode
                => global::KeyLoad.ProcessRecoveryTests.Messaging.TargetInboxCrashScenario.RunAsync(directory, store, boundary),
            global::KeyLoad.ProcessRecoveryTests.Messaging.LocalInboxProcessingCrashContract.Mode
                => global::KeyLoad.ProcessRecoveryTests.Messaging.LocalInboxProcessingCrashScenario.RunAsync(directory, store, boundary),
            CrashFixtureValues.ProjectionMode => ProjectionCrashScenario.RunAsync(directory, store, boundary),
            CrashFixtureValues.SubscriptionMode => SubscriptionCrashScenario.RunAsync(directory, store, boundary),
            SubscriptionFilterCrashScenario.Mode => SubscriptionFilterCrashScenario.RunAsync(directory, store, boundary),
            DatabaseCompositionCrashScenario.Mode => DatabaseCompositionCrashScenario.RunAsync(directory, store, boundary),
            AggregateReplayCrashScenario.Mode => AggregateReplayCrashScenario.RunAsync(directory, store, boundary),
            EventAppendCrashContract.Mode => EventAppendCrashScenario.RunAsync(directory, store, boundary),
            global::KeyLoad.ProcessRecoveryTests.EventStreams.SharedDomainCrashProtocol.Mode
                => global::KeyLoad.ProcessRecoveryTests.EventStreams.SharedDomainCrashScenario.RunAsync(directory, store, boundary),
            TopicPurgeCrashContract.Mode => TopicPurgeCrashScenario.RunAsync(directory, store, boundary, false),
            TopicPurgeCrashContract.Mode + TopicPurgeCrashContract.PinnedSuffix => TopicPurgeCrashScenario.RunAsync(directory, store, boundary, true),
            SampleRollupCrashContract.PrepareMode or SampleRollupCrashContract.RefreshFaultMode
                or SampleRollupCrashContract.DropFaultMode or SampleRollupCrashContract.RefreshRecoverMode
                or SampleRollupCrashContract.DropRecoverMode or SampleRollupCrashContract.VerifyMode
                => SampleRollupCrashScenario.RunAsync(directory, store, boundary, mode),
            AnnCrashContract.Prepare or AnnCrashContract.Fault or AnnCrashContract.Recover or AnnCrashContract.Verify
                => AnnCrashScenario.RunAsync(directory, store, boundary, mode),
            SampleRetentionCrashScenario.Mode => SampleRetentionCrashScenario.RunAsync(directory, store, boundary),
            EventProjectionCrashScenario.Mode => EventProjectionCrashScenario.RunAsync(directory, store, boundary),
            QueueDeadlineCrashProtocol.Mode => QueueDeadlineCrashScenario.RunAsync(directory, store, boundary),
            QueueEnqueueCrashProtocol.Mode => QueueEnqueueCrashScenario.RunAsync(directory, store, boundary),
            QueueOrderedRetryCrashProtocol.Mode => QueueOrderedRetryCrashScenario.RunAsync(directory, store, boundary),
            QueueClaimAckCrashProtocol.ClaimMode => QueueClaimAckCrashScenario.RunAsync(directory, store, boundary, false),
            QueueClaimAckCrashProtocol.AckMode => QueueClaimAckCrashScenario.RunAsync(directory, store, boundary, true),
            QueueLifecycleCrashProtocol.Mode => QueueLifecycleCrashScenario.RunAsync(directory, store, boundary),
            RecurringScheduleCrashScenario.Mode => RecurringScheduleCrashScenario.RunAsync(directory, store, boundary),
            SagaTimeoutCrashScenario.Mode => SagaTimeoutCrashScenario.RunAsync(directory, store, boundary),
            CommandIdempotencyCrashContract.FirstMode => CommandIdempotencyCrashScenario.RunFirstAsync(directory, store),
            CommandIdempotencyCrashContract.ReplayMode => CommandIdempotencyCrashScenario.RunReplayAsync(directory, store),
            CompositeIndexCrashContract.PrepareMode or CompositeIndexCrashContract.FaultMode
                or CompositeIndexCrashContract.RecoverMode or CompositeIndexCrashContract.VerifyMode
                => CompositeIndexCrashScenario.RunAsync(directory, store, boundary, mode),
            ScalarIndexCrashContract.PrepareMode or ScalarIndexCrashContract.FaultMode
                or ScalarIndexCrashContract.RecoverMode or ScalarIndexCrashContract.VerifyMode
                => ScalarIndexCrashScenario.RunAsync(directory, store, boundary, mode),
            _ => RunCanonicalAsync(directory, store, boundary, mode)
        };

    private static async Task RunCanonicalAsync(string directory, ZoneTreeStore store,
        CanonicalCrashBoundary boundary, string mode)
    {
        CanonicalCrashScenario.Run(directory, store, boundary, mode);
        await RecoveryProcessPause.WaitForKillAsync();
    }
}
