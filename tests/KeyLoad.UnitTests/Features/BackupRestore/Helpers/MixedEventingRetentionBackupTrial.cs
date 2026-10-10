using System.Security.Cryptography;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class MixedEventingRetentionBackupTrial
{
    internal static Task RunAsync(CancellationToken token) => EventingArtifactFixture.RunAsync(async fixture =>
    {
        var state = await MixedEventingRetentionBackupSeed.RunAsync(fixture, token);
        await MixedEventingRetentionBackupArchive.RestoreAsync(fixture, state, token);
        await MixedEventingRetentionBackupContinuation.RunAsync(fixture, state, token);
        var finalRows = MixedEventingRetentionBackupOracle.Rows(fixture.Target!, state);
        var finalPhysical = EventingArtifactState.FullBytes(fixture.Target!);
        var identity = fixture.Target!.Identity;
        var signingFingerprint = SHA256.HashData(identity.SigningKey.Span);
        var cut = fixture.Target.Position;
        fixture.ReopenTarget(Path.Combine(fixture.Root, MixedEventingRetentionBackupProtocol.Target));
        await Assert.That(fixture.Target!.Identity with { SigningKey = ReadOnlyMemory<byte>.Empty })
            .IsEqualTo(identity with { SigningKey = ReadOnlyMemory<byte>.Empty });
        await Assert.That(SHA256.HashData(fixture.Target.Identity.SigningKey.Span))
            .IsEquivalentTo(signingFingerprint, CollectionOrdering.Matching);
        await Assert.That(fixture.Target.Position).IsEqualTo(cut);
        await MixedEventingRetentionBackupOracle.SameAsync(finalRows, fixture.Target, state);
        await Assert.That(EventingArtifactState.FullBytes(fixture.Target)).IsEquivalentTo(finalPhysical, CollectionOrdering.Matching);
        await QueueLifecycleOperations.ReplayAsync(fixture.Database, state.RestoredQueue);
        await QueueLifecycleAccountingAssertions.TerminalAsync(fixture.Target, state.RestoredQueue, true);
        await EventingArtifactHealthyAssertions.CompletedAfterNaturalExpiryAsync(fixture, fixture.Database);
        await MixedEventingRetentionBackupTopic.RequireAsync(fixture.Database, state);
        await TargetInboxNativeAssertions.CapacityAsync(fixture.Target, state.Inbox, TargetInboxUnitProtocol.ReceiptCapacity);
        await Assert.That(TargetInboxNativeSetup.Apply(fixture.Database, state.Inbox, token).Error).IsEqualTo(ErrorCode.TokenInvalidated);
        await MixedEventingRetentionBackupTopic.EqualAsync(state.HealthyInboxResult,
            fixture.ApplyInbox(fixture.Database, state.HealthyInbox, token).Get<CommitInboxResult>());
        await MixedEventingRetentionBackupPrivacy.RequireAsync(fixture.Database, state);
        await ColdHealthyAsync(fixture);
        await MixedEventingRetentionBackupArchive.SourceAsync(fixture, state, token);
    }, nativeAdmission: true);

    private static async Task ColdHealthyAsync(EventingArtifactFixture fixture)
    {
        var command = new CommandRequest(Guid.NewGuid(), fixture.Source.Partition,
            [new PutDocument(EventingArtifactFixture.Documents, "mixed-cold-healthy", EventingArtifactFixture.Json, 0)]);
        var receipt = fixture.Apply(fixture.Database, OperationKind.Batch, command, command.CommandId).Get<CommitReceipt>();
        var cut = fixture.Target!.Position;
        await MixedEventingRetentionBackupTopic.EqualAsync(receipt,
            fixture.Apply(fixture.Database, OperationKind.Batch, command, command.CommandId).Get<CommitReceipt>());
        await Assert.That(fixture.Target.Position).IsEqualTo(cut);
        var actual = fixture.Database.GetDocument(EventingArtifactFixture.Principal,
            new(fixture.Source.Partition, EventingArtifactFixture.Documents, "mixed-cold-healthy"));
        await Assert.That(actual!.Revision).IsEqualTo(1L);
        await Assert.That(actual.Json).IsEqualTo(EventingArtifactFixture.Json);
        await Assert.That(actual.Redacted).IsFalse();
        await Assert.That(actual.RedactedFields).IsEmpty();
    }
}
