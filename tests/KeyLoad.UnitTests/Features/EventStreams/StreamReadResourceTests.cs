using System.Text.Json;
namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class StreamReadResourceTests
{
    [Test]
    public async Task AcMp005EmptyPageFitsItsExactSerializedByteLimit()
    {
        using var fixture = new StreamReadResourceFixture();
        var expected = fixture.Read();
        var exactBytes = JsonSerializer.SerializeToUtf8Bytes(expected, JsonDefaults.Options).Length;
        fixture.Reopen(new() { MaxBatchBytes = exactBytes });

        var actual = fixture.Read();

        await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
    }

    [Test]
    public async Task AcMp005CompletePageMetadataCountsTowardTheResultLimit()
    {
        using var fixture = new StreamReadResourceFixture(eventCount: 1);
        var expected = fixture.Read();
        var exactBytes = JsonSerializer.SerializeToUtf8Bytes(expected, JsonDefaults.Options).Length;
        fixture.Reopen(new() { MaxBatchBytes = exactBytes - 1 });

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Read());

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task AcMp005RangePreservesRevisionOrderCutAndLookahead()
    {
        using var fixture = new StreamReadResourceFixture(eventCount: 3);

        var firstPage = fixture.Read(limit: 2);
        var secondPage = fixture.Read(afterRevision: 2, limit: 2);

        await Assert.That(firstPage.Events.Select(record => record.Revision).SequenceEqual([1L, 2L])).IsTrue();
        await Assert.That(firstPage.HasMore).IsTrue();
        await Assert.That(secondPage.Events.Select(record => record.Revision).SequenceEqual([3L])).IsTrue();
        await Assert.That(secondPage.HasMore).IsFalse();
        await Assert.That(firstPage.CutPosition).IsEqualTo(secondPage.CutPosition);
    }

    [Test]
    public async Task AcMp005RangeWorkStopsBeforeTheNextEventAndAHealthyReadStillWorks()
    {
        using var fixture = new StreamReadResourceFixture(eventCount: 3);
        var headAndFirstEventBytes = fixture.HeadAndFirstEventReadBytes();
        fixture.Reopen(new() { MaxQueryReadBytes = headAndFirstEventBytes - 1 });

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Read());
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);

        fixture.Reopen(new());
        await Assert.That(fixture.Read().Events.Length).IsEqualTo(3);
    }

    [Test]
    public async Task AcMp005CallerCancellationDoesNotPoisonTheFollowingRead()
    {
        using var fixture = new StreamReadResourceFixture(eventCount: 1);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Assert.ThrowsExactly<OperationCanceledException>(() => fixture.Read(cancellationToken: cancellation.Token));

        await Assert.That(fixture.Read().Events).HasSingleItem();
    }

    [Test]
    public async Task AcMp005PersistedFieldPoliciesRedactPayloadAndHeaders()
    {
        using var fixture = new StreamReadResourceFixture(eventCount: 1, protectedFields: true);

        var result = fixture.Read().Events.Single();

        await Assert.That(result.Data.PayloadJson).DoesNotContain(StreamReadResourceFixture.ProtectedValue);
        await Assert.That(result.Data.HeadersJson).DoesNotContain(StreamReadResourceFixture.ProtectedHeaderValue);
        await Assert.That(result.Data.PayloadJson).Contains(StreamReadResourceFixture.PublicValue);
    }

    [Test]
    public async Task AcMp005GenerationAndRetentionFailuresRemainCallerVisible()
    {
        using var fixture = new StreamReadResourceFixture(eventCount: 1);
        var staleGeneration = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Read(generation: 2));
        await Assert.That(staleGeneration.Code).IsEqualTo(ErrorCode.TokenInvalidated);

        fixture.RetainFromRevision(2);
        var oldRevision = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Read(afterRevision: 0));
        await Assert.That(oldRevision.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
    }
}
