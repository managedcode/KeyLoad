using KeyLoad.FunctionalTests.Authorization;
using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.Storage.ZoneTree;
using static KeyLoad.FunctionalTests.ResourceExecution.ResourcePairBindingState;

namespace KeyLoad.FunctionalTests.ResourceExecution;

internal static class ResourcePairBindingCold
{
    internal static async Task RunAsync(ResourcePairBindingState state, CancellationToken token)
    {
        var identity = state.Source.Store.Identity;
        var image = QueueWholeFlowStorage.Bytes(state.Source.Store);
        var cut = state.Source.ReadCanonicalCut();
        state.Source.JoinOwnedResources();
        for (var wave = Zero; wave < TwoColdWaves; wave += One)
        {
            token.ThrowIfCancellationRequested();
            await ResourceIncarnationCold.OpenAsync(state.Source, identity, async store =>
            {
                await Assert.That(OrleansSerialization.Serialize(identity).AsSpan()
                    .SequenceEqual(OrleansSerialization.Serialize(store.Identity))).IsTrue();
                await ResourceIncarnationAssertions.UnchangedAsync(store, image, cut.StorePosition);
                var database = state.Source.CreateConfiguredDatabase(store);
                await Assert.That(database.LastApplied).IsEqualTo(cut.AppliedPosition);
                await ReplayAsync(state, store, database);
                await ResourcePairBindingAssertions.ModelsAsync(state, database);
                await ResourcePairBindingSql.HealthyAsync(state, database, token);
                await ResourcePairBindingQuery.HealthyAsync(state, database);
                await ResourcePairBindingBm25.HealthyAsync(state, database, token, expectedCurrent: wave == Zero);
                var request = ResourcePairBindingBatch.Request(state.Source.Partition,
                    ColdPrefix + wave.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var expectedIndex = database.LastApplied + One;
                var issued = ResourcePairBindingBatch.Issue(database, request);
                var result = database.ApplyEmbedded(issued, token);
                var original = (OutcomeStoreOracle.ReadStored(store, issued)
                    ?? throw new InvalidOperationException(Invalid)).PreparedCommand;
                await ResourcePairBindingBatch.AcceptAsync(state, database, store, request, original, result, expectedIndex);
                state.Remember(original, result, store);
                await ResourcePairBindingAssertions.PairsAsync(state, store);
                image = QueueWholeFlowStorage.Bytes(store);
                cut = store.Read(view => (view.ReadOwnedValue(KeySpace.AppliedBytes) is { } bytes
                    ? OrleansSerialization.Deserialize<long>(bytes) : default(long), store.Position));
            });
        }
    }

    private static async Task ReplayAsync(ResourcePairBindingState state, ZoneTreeStore store, DatabaseEngine database)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var applied = database.LastApplied;
        foreach (var value in state.Originals)
        {
            await ResourceIncarnationAssertions.ResultAsync(value.Result, database.Apply(value.Original));
            await Assert.That(RequiredProcessingPolicyAssertions.Outcome(store, state.Source.Partition,
                value.Original.UserId!, value.Original.CommandId).AsSpan().SequenceEqual(value.Outcome)).IsTrue();
        }
        await ResourceIncarnationAssertions.UnchangedAsync(store, image, position);
        await Assert.That(database.LastApplied).IsEqualTo(applied);
        await ResourcePairBindingAssertions.PairsAsync(state, store);
    }
}
