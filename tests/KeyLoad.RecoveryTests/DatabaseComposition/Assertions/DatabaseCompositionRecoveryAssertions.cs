using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.CrashHost;

namespace KeyLoad.RecoveryTests.Features.DatabaseComposition;

internal static class DatabaseCompositionRecoveryAssertions
{
    internal static async Task AssertSeedUnchangedAsync(DatabaseEngine database)
    {
        var partition = DatabaseCompositionCrashScenario.Partition;
        foreach (var id in new[] { DatabaseCompositionCrashScenario.First, DatabaseCompositionCrashScenario.Second })
        {
            await Assert.That(database.GetDocument(DatabaseCompositionCrashScenario.Principal,
                new(partition, DatabaseCompositionCrashScenario.Collection, id))).IsNotNull();
        }
        var source = database.InspectMessage(DatabaseCompositionCrashScenario.Principal,
            new(partition, DatabaseCompositionCrashScenario.SourceQueue),
            DatabaseCompositionCrashScenario.SourceMessage);
        await Assert.That(source).IsNotNull();
        await Assert.That(source!.Metadata.State).IsEqualTo(MessageState.Ready);
    }

    internal static async Task AssertCommittedEffectsAsync(DatabaseEngine database, long seedTail, CommitReceipt receipt)
    {
        var fixture = DatabaseCompositionCrashScenario.Partition;
        var from = new EntityRef(fixture, DatabaseCompositionCrashScenario.Collection,
            DatabaseCompositionCrashScenario.First);
        var to = new EntityRef(fixture, DatabaseCompositionCrashScenario.Collection,
            DatabaseCompositionCrashScenario.Second);
        var edgeId = DatabaseCompositionCrashScenario.EdgePrefix + DatabaseCompositionCrashScenario.SourceMessage;
        var graph = database.Traverse(DatabaseCompositionCrashScenario.Principal, fixture,
            DatabaseCompositionCrashScenario.Graph, from);
        var message = database.InspectMessage(DatabaseCompositionCrashScenario.Principal,
            new(fixture, DatabaseCompositionCrashScenario.TargetQueue),
            DatabaseCompositionCrashScenario.MessagePrefix + edgeId);
        await Assert.That(graph.Edges).HasSingleItem();
        await Assert.That(graph.Edges[0].Id).IsEqualTo(edgeId);
        await Assert.That(graph.Edges[0].From).IsEqualTo(from);
        await Assert.That(graph.Edges[0].To).IsEqualTo(to);
        await Assert.That(message).IsNotNull();
        await Assert.That(message!.Metadata.State).IsEqualTo(MessageState.Ready);
        var link = JsonSerializer.Deserialize<QueueGraphLink>(message.PayloadJson!, JsonDefaults.Options);
        await Assert.That(link).IsNotNull();
        await Assert.That(link!.From).IsEqualTo(from);
        await Assert.That(link.To).IsEqualTo(to);
        await Assert.That(link.Label).IsEqualTo(DatabaseCompositionCrashScenario.Label);
        await Assert.That(link.AttributesJson).IsEqualTo("{}");
        await Assert.That(receipt.Mutations.Select(item => item.Kind).SequenceEqual(["upsertEdge", "enqueue"]))
            .IsTrue();
        await Assert.That(database.GetOutboxStatus(DatabaseCompositionCrashScenario.Principal, fixture).Head.Tail)
            .IsEqualTo(seedTail + 2);
    }

    internal static async Task AssertStableReplayAsync(DatabaseEngine database, ReplicatedOperation operation,
        CommitReceipt first)
    {
        var partition = DatabaseCompositionCrashScenario.Partition;
        var previousTail = database.GetOutboxStatus(DatabaseCompositionCrashScenario.Principal, partition).Head.Tail;
        var second = database.Apply(operation).Get<CommitReceipt>();
        var resolved = database.ResolveOutcome(operation).Get<CommitReceipt>();
        await Assert.That(second.Token).IsEqualTo(first.Token);
        await Assert.That(resolved.Token).IsEqualTo(first.Token);
        await Assert.That(SameEffects(second, first)).IsTrue();
        await Assert.That(SameEffects(resolved, first)).IsTrue();
        await Assert.That(database.GetOutboxStatus(DatabaseCompositionCrashScenario.Principal, partition).Head.Tail)
            .IsEqualTo(previousTail);
        await Assert.That(database.Traverse(DatabaseCompositionCrashScenario.Principal, partition,
            DatabaseCompositionCrashScenario.Graph,
            new(partition, DatabaseCompositionCrashScenario.Collection, DatabaseCompositionCrashScenario.First))
            .Edges).HasSingleItem();
    }

    private static bool SameEffects(CommitReceipt left, CommitReceipt right)
        => left.Mutations.Select(effect => (effect.Kind, effect.Resource, effect.Id, effect.Revision))
            .SequenceEqual(right.Mutations.Select(effect => (effect.Kind, effect.Resource, effect.Id, effect.Revision)));
}
