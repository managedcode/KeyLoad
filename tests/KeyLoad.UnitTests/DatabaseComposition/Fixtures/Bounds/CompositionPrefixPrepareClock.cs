using D = KeyLoad.UnitTests.DatabaseComposition.CompositionPrefixPrepareData;

namespace KeyLoad.UnitTests.DatabaseComposition;

internal static class CompositionPrefixPrepareClock
{
    internal static Task RunAsync(bool budget, CancellationToken token)
        => CompositionPrefixPrepareOwner.RunAsync(null, async owner =>
        {
            await CompositionPrefixPrepareSeed.ConfigureAsync(owner);
            await CompositionPrefixPrepareSeed.VerticesAsync(owner);
            foreach (var id in budget ? new[] { D.First, D.Second } : new[] { D.Message })
            {
                await CompositionPrefixPrepareCommands.CommitAsync(owner,
                    [new EnqueueMessage(D.Queue, id, budget ? CompositionPrefixPrepareScenarios.Link(owner) : D.EmptyJson)],
                    [new(D.Enqueue, D.Queue, id, D.FirstRevision)]);
            }
            var image = CompositionPrefixPrepareAssertions.ModelImage(owner);
            // UnixEpoch is an actual command time older than the committed database clock.
            var stale = CompositionPrefixPrepareCommands.Issue(owner,
                [new PutDocument(D.From, D.Marker, D.MarkerJson, D.Absent),
                 CompositionPrefixPrepareCommands.Forward(typed: false)], evaluatedAt: DateTimeOffset.UnixEpoch);
            stale = stale with
            {
                Error = ErrorCode.ClockUncertain,
                SafeDetail = D.ClockBehind
            };
            await CompositionPrefixPrepareAssertions.ResultAsync(stale, owner.Submit(stale.Original));
            owner.Commands.Add(stale);
            await Assert.That(CompositionPrefixPrepareAssertions.ModelImage(owner).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
            var fresh = CompositionPrefixPrepareCommands.Issue(owner,
                [new PutDocument(D.From, D.Marker, D.MarkerJson, D.Absent),
                 CompositionPrefixPrepareCommands.Forward(typed: false)]);
            fresh = fresh with
            {
                Error = budget ? ErrorCode.BudgetExceeded : ErrorCode.Validation,
                SafeDetail = budget ? D.SourceOverflow : D.InvalidLink
            };
            await CompositionPrefixPrepareAssertions.ResultAsync(fresh, owner.Submit(fresh.Original));
            owner.Commands.Add(fresh);
            await Assert.That(CompositionPrefixPrepareAssertions.ModelImage(owner).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
            await CompositionPrefixPrepareAssertions.ColdAsync(owner, async () =>
            {
                await Assert.That(CompositionPrefixPrepareAssertions.ModelImage(owner).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
            });
        }, token);

}
