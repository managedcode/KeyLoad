using System.Globalization;
using KeyLoad.UnitTests.Features.ResourceExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class EventAppendWholeFlowTests
{
    [Test]
    [Arguments(2)]
    [Arguments(8)]
    public async Task AcEvent004ConcurrentExactAppendHasOneWholeProducerAndStableReplay(int contenders)
    {
        using var fixture = new EventAppendWholeFlowFixture();
        var commands = Enumerable.Range(0, contenders).Select(index => fixture.Producer(
            "producer-" + index.ToString(CultureInfo.InvariantCulture), ExpectedStreamRevision.Exact(1),
            EventAppendWholeFlowFixture.Data("event-" + index.ToString(CultureInfo.InvariantCulture)))).ToArray();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workers = commands.Select(command => Task.Run(async () =>
        {
            await start.Task.ConfigureAwait(false);
            return fixture.Submit(command);
        })).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(workers).ConfigureAwait(false);
        var winner = await Assert.That(Enumerable.Range(0, contenders).Where(index => results[index].Error is null)).HasSingleItem();
        var winningEvent = ((AppendEvents)commands[winner].Mutations[^1]).Events.Single();
        await fixture.AssertPageAsync(EventAppendWholeFlowFixture.Data(EventAppendWholeFlowFixture.InitialId), winningEvent);
        for (var index = 0; index < contenders; index++)
        {
            if (index != winner)
            {
                await Assert.That(results[index].Error).IsEqualTo(ErrorCode.RevisionConflict);
                await Assert.That(results[index].SafeDetail).IsEqualTo("The expected stream revision does not match.");
            }
            await fixture.AssertProducerAsync("producer-" + index.ToString(CultureInfo.InvariantCulture), index == winner);
        }
        var bytes = fixture.DomainBytes();
        for (var index = 0; index < contenders; index++)
        {
            if (index == winner)
            {
                await NativeReplayResultAssertions.Same<CommitReceipt>(fixture.Submit(commands[index]), results[index]);
            }
            else
            {
                await fixture.AssertFailedReplayAsync(commands[index], results[index]);
            }
        }
        await Assert.That(fixture.DomainBytes()).IsEquivalentTo(bytes, CollectionOrdering.Matching);
        var healthy = fixture.Producer("healthy", ExpectedStreamRevision.Exact(2), EventAppendWholeFlowFixture.Data("healthy-event"));
        fixture.Submit(healthy).Get<CommitReceipt>();
        await fixture.AssertProducerAsync("healthy", true);
        await fixture.AssertPageAsync(EventAppendWholeFlowFixture.Data(EventAppendWholeFlowFixture.InitialId),
            winningEvent, EventAppendWholeFlowFixture.Data("healthy-event"));
    }

    [Test]
    [Arguments("no-stream", ErrorCode.RevisionConflict, "The expected stream revision does not match.")]
    [Arguments("stale-exact", ErrorCode.RevisionConflict, "The expected stream revision does not match.")]
    [Arguments("same-event", ErrorCode.DuplicateEventId, "The event ID is already retained in this source generation.")]
    [Arguments("changed-event", ErrorCode.Conflict, "The event ID was reused with different content.")]
    public async Task AcEvent004And005RejectedAppendRollsBackWholeProducerAndReplaysFailure(string kind,
        ErrorCode expectedError, string expectedDetail)
    {
        using var fixture = new EventAppendWholeFlowFixture();
        var condition = kind switch
        {
            "no-stream" => ExpectedStreamRevision.NoStream,
            "stale-exact" => ExpectedStreamRevision.Exact(0),
            _ => ExpectedStreamRevision.Any
        };
        var events = kind is "same-event" or "changed-event"
            ? new[] { EventAppendWholeFlowFixture.Data("rolled-back-event"),
                EventAppendWholeFlowFixture.Data(EventAppendWholeFlowFixture.InitialId, kind == "changed-event" ? "{\"changed\":true}" : "{}") }
            : [EventAppendWholeFlowFixture.Data("rolled-back-event")];
        var command = fixture.Producer("rolled-back-producer", condition, events);
        var before = fixture.DomainBytes();
        var failed = fixture.Submit(command);
        await Assert.That(failed.Error).IsEqualTo(expectedError);
        await Assert.That(failed.SafeDetail).IsEqualTo(expectedDetail);
        await Assert.That(fixture.DomainBytes()).IsEquivalentTo(before, CollectionOrdering.Matching);
        await fixture.AssertProducerAsync("rolled-back-producer", false);
        await fixture.AssertPageAsync(EventAppendWholeFlowFixture.Data(EventAppendWholeFlowFixture.InitialId));
        await fixture.AssertFailedReplayAsync(command, failed);
        await Assert.That(fixture.DomainBytes()).IsEquivalentTo(before, CollectionOrdering.Matching);
        var healthy = fixture.Producer("healthy", ExpectedStreamRevision.Exact(1), EventAppendWholeFlowFixture.Data("healthy-event"));
        fixture.Submit(healthy).Get<CommitReceipt>();
        await fixture.AssertProducerAsync("healthy", true);
        await fixture.AssertPageAsync(EventAppendWholeFlowFixture.Data(EventAppendWholeFlowFixture.InitialId),
            EventAppendWholeFlowFixture.Data("healthy-event"));
    }
}
