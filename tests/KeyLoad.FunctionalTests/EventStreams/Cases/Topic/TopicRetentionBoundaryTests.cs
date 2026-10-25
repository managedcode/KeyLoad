
namespace KeyLoad.FunctionalTests.EventStreams;

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

}
