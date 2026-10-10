using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class MixedEventingRetentionBackupSeed
{
    internal static async Task<MixedEventingRetentionBackupState> RunAsync(EventingArtifactFixture fixture, CancellationToken token)
    {
        await EventingArtifactSeed.RunAsync(fixture);
        QueueLifecycleProvisioning.Seed(fixture.Source, byteSublimit: false);
        var state = new MixedEventingRetentionBackupState
        {
            Queue = new(fixture.Source.Partition, QueueWholeFlowStorage.Clock(fixture.Source.Store))
            { NativeSubmit = fixture.Source.SubmitIssued }
        };
        await QueueLifecycleInitialPhase.RunAsync(fixture.Source.Database, fixture.Source.Store, state.Queue, token);
        var originalFailures = new List<Exception>();
        await MixedEventingJournalRecovery.RunAsync(fixture.Source, state.Queue, originalFailures, token);
        await Assert.That(originalFailures).IsNotEmpty();
        state.OriginalJournalFailures = originalFailures.ToArray();
        await MixedEventingRetentionBackupTopic.SeedAsync(fixture.Source, state, token);
        var inbox = TargetInboxNativeSetup.Create(fixture.Source);
        state.Inbox = inbox.Request;
        state.InboxSource = inbox.Source;
        state.InboxDelivery = inbox.Delivery;
        state.InboxResult = fixture.ApplyInbox(fixture.Source.Database, inbox.Request, token).Get<CommitInboxResult>();
        await Assert.That(state.InboxResult.AlreadyProcessed).IsFalse();
        await TargetInboxNativeAssertions.EffectsAsync(fixture.Source.Database, inbox.Request);
        await TargetInboxNativeAssertions.CapacityAsync(fixture.Source.Store, inbox.Request, TargetInboxUnitProtocol.FirstRevision);
        state.Outbox = fixture.Source.Database.GetOutboxStatus(EventingArtifactFixture.Principal, fixture.Source.Partition);
        await MixedEventingRetentionBackupOracle.InitialAsync(fixture, state, fixture.Source.Database);
        return state;
    }
}
