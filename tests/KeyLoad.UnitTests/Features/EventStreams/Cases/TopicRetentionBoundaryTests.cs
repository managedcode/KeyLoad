using KeyLoad.Core;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class TopicRetentionBoundaryTests
{
    [Test]
    [Arguments(0L, 1L, ErrorCode.Validation)]
    [Arguments(4L, 1L, ErrorCode.Validation)]
    [Arguments(2L, 2L, ErrorCode.TokenInvalidated)]
    public async Task AcEventRetention002InvalidCutAndGenerationRetainAllModels(long cut, long generation, ErrorCode error)
    {
        using var fixture = new TopicRetentionFixture();
        await TopicRetentionAssertions.Reject(fixture, fixture.Purge(cut, generation), error);
        fixture.ReleasePins();
        fixture.Submit(fixture.Purge()).Get<CommitReceipt>();
        await TopicRetentionIdentityFlow.VerifyAsync(fixture);
    }

    [Test]
    public async Task AcEventRetention002MissingFreshGrantDeniesPurgeAndPrivilegedReceiptReplay()
    {
        using var fixture = new TopicRetentionFixture();
        fixture.Owner.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(
            new("reader", "tenant", [new("database", "topic", Capability.TopicsRead)], []))).Get<PrincipalRecord>();
        await TopicRetentionAssertions.Reject(fixture, fixture.Purge(), ErrorCode.PermissionDenied, "reader");
        fixture.ReleasePins();
        var command = fixture.Purge();
        var receipt = fixture.Submit(command).Get<CommitReceipt>();
        await TopicRetentionPostPurgeRejection.RejectAsync(fixture, command, ErrorCode.PermissionDenied, "reader");
        var replay = fixture.Submit(command).Get<CommitReceipt>();
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
        await TopicRetentionIdentityFlow.VerifyAsync(fixture);
    }

    [Test]
    public async Task AcEventRetention002CompletePinScanCannotSilentlySkipGroupsAtRecordLimit()
    {
        using var fixture = new TopicRetentionFixture();
        fixture.ReleasePins();
        fixture.SetLimits(new() { MaxScanRecords = 1 });
        await TopicRetentionAssertions.Reject(fixture, fixture.Purge(), ErrorCode.ResourceExhausted);
        fixture.SetLimits(new());
        fixture.Submit(fixture.Purge()).Get<CommitReceipt>();
        await TopicRetentionIdentityFlow.VerifyAsync(fixture);
    }

    [Test]
    public async Task AcEventRetention002NativePinBytesAreChargedBeforeRetentionDecision()
    {
        using var fixture = new TopicRetentionFixture();
        fixture.ReleasePins();
        _ = SubscriptionTestActions.Configure(fixture.Owner, "large", start: SubscriptionStart.FromNow,
            types: ["Created", .. Enumerable.Range(0, 120).Select(index => "EventType" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + new string('x', 40))]);
        var key = KeySpace.Partition("subscription", fixture.Owner.Partition, "topic", "Topic", null, 1L, "large");
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(key)!.Length)).IsGreaterThan(4096);
        fixture.SetLimits(new() { MaxBatchBytes = 4096 });
        await TopicRetentionAssertions.Reject(fixture, fixture.Purge(), ErrorCode.ResourceExhausted);
        fixture.SetLimits(new());
        fixture.Submit(fixture.Purge()).Get<CommitReceipt>();
        await TopicRetentionIdentityFlow.VerifyAsync(fixture);
    }
}
