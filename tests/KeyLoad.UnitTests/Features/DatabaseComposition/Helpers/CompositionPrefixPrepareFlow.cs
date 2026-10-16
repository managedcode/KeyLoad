using D = KeyLoad.UnitTests.Features.DatabaseComposition.CompositionPrefixPrepareData;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal static class CompositionPrefixPrepareFlow
{
    internal static Task RunAsync(int scenario, CancellationToken token)
        => CompositionPrefixPrepareOwner.RunAsync(null, async owner =>
        {
            await CompositionPrefixPrepareSeed.ConfigureAsync(owner);
            switch (scenario)
            {
                case D.Absent:
                    await CompositionPrefixPrepareScenarios.StagedPutAsync(owner);
                    break;
                case D.FirstRevision:
                    await CompositionPrefixPrepareScenarios.CancelledAsync(owner);
                    break;
                case D.CancelRevision:
                    await CompositionPrefixPrepareScenarios.ReverseAsync(owner);
                    break;
                case D.EffectCap:
                    await CompositionPrefixPrepareScenarios.StagedPutAsync(owner, typed: false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario));
            }
        }, token);

    internal static Task TemporalAsync(bool expired, CancellationToken token)
        => CompositionPrefixPrepareOwner.RunAsync(null, async owner =>
        {
            await CompositionPrefixPrepareSeed.ConfigureAsync(owner);
            await CompositionPrefixPrepareSeed.VerticesAsync(owner);
            var boundary = owner.Database.EvaluationClock.GetUtcNow().AddSeconds(D.TemporalSeconds);
            if (expired)
            {
                await CompositionPrefixPrepareCommands.CommitAsync(owner,
                    [new EnqueueMessage(D.Queue, D.Message, D.EmptyJson, ExpiresAt: boundary)],
                    [new(D.Enqueue, D.Queue, D.Message, D.FirstRevision)]);
                var remaining = boundary - owner.Database.EvaluationClock.GetUtcNow();
                while (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, token);
                    remaining = boundary - owner.Database.EvaluationClock.GetUtcNow();
                }
                await CompositionPrefixPrepareCommands.CommitAsync(owner,
                    [CompositionPrefixPrepareCommands.Forward()], []);
            }
            else
            {
                await CompositionPrefixPrepareCommands.CommitAsync(owner,
                    [new EnqueueMessage(D.Queue, D.Message, D.EmptyJson, NotBefore: boundary),
                     CompositionPrefixPrepareCommands.Forward()],
                    [new(D.Enqueue, D.Queue, D.Message, D.FirstRevision)]);
            }
            await CompositionPrefixPrepareAssertions.ColdAsync(owner, async () =>
            {
                await Assert.That(owner.Database.Traverse(D.Root, owner.Source.Partition, D.Graph,
                    CompositionPrefixPrepareSeed.Fresh(owner, D.From, D.First)).Edges).IsEmpty();
                var message = owner.Database.InspectMessage(D.Root, new(owner.Source.Partition, D.Queue), D.Message)!;
                await Assert.That(message.Metadata.State).IsEqualTo(expired ? MessageState.Ready : MessageState.Scheduled);
                await Assert.That(message.PayloadJson).IsEqualTo(D.EmptyJson);
                await Assert.That(message.Metadata.ExpiresAt).IsEqualTo(expired ? boundary : null);
                await Assert.That(message.Metadata.NotBefore).IsEqualTo(expired ? null : boundary);
            });
        }, token);

    internal static Task CapacityAsync(CancellationToken token)
        => CompositionPrefixPrepareOwner.RunAsync(new()
        {
            MaxBatchMutations = D.EffectCap
        }, async owner =>
        {
            await CompositionPrefixPrepareSeed.ConfigureAsync(owner);
            await CompositionPrefixPrepareSeed.VerticesAsync(owner);
            await CompositionPrefixPrepareCommands.CommitAsync(owner,
                [new EnqueueMessage(D.Queue, D.Message, CompositionPrefixPrepareScenarios.Link(owner))],
                [new(D.Enqueue, D.Queue, D.Message, D.FirstRevision)]);
            await CompositionPrefixPrepareCommands.CommitAsync(owner,
                [CompositionPrefixPrepareCommands.Forward()],
                [CompositionPrefixPrepareCommands.Edge(owner, D.Graph, D.Forward + D.Message)], D.BindingCount);
            await CompositionPrefixPrepareRefusals.LateConflictAsync(owner);
            await CompositionPrefixPrepareAssertions.ColdAsync(owner,
                () => CompositionPrefixPrepareAssertions.EdgeAsync(owner, D.Graph, D.Forward + D.Message));
        }, token);
}
