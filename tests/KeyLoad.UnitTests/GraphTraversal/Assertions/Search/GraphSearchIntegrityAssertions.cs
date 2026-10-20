using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using P = KeyLoad.UnitTests.Features.GraphTraversal.GraphTraversalIntegrityFixture;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphSearchIntegrityAssertions
{
    private const string InvalidEdge = "A committed graph cross-partition record is malformed.";
    private const string InvalidAdjacency = "A graph adjacency record does not match its canonical edge.";
    private const string MissingOutcome = "The original graph-search fixture outcome is missing.";

    internal static async Task ReadAsync(DatabaseEngine owner, ZoneTreeStore store,
        GraphTraversalIntegrityState state, GraphTraversalIntegrityContinuation? continuation,
        GraphSearchIntegrityOperator selected, bool filtered, CancellationToken token)
    {
        var image = CanonicalStoreImage.Bytes(store);
        var cut = store.Position;
        var actual = await new SearchEngine(owner, UnitExecutionOptions.QueryExecution()).GraphSearchAsync(
            P.Root, GraphSearchIntegrityRecipe.Request(state, selected, filtered), token);
        await EqualAsync(GraphSearchIntegrityRecipe.Expected(state, continuation, selected, filtered), actual);
        await GraphTraversalIntegrityAssertions.Image(store, image, cut);
    }

    internal static async Task RefuseAsync(DatabaseEngine owner, ZoneTreeStore store,
        GraphTraversalIntegrityState state, GraphSearchIntegrityOperator selected, bool filtered,
        GraphTraversalIntegrityFault fault, CancellationToken token)
    {
        var image = CanonicalStoreImage.Bytes(store);
        var cut = store.Position;
        GraphSearchResult? result = null;
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () =>
        {
            result = await new SearchEngine(owner, UnitExecutionOptions.QueryExecution()).GraphSearchAsync(
                P.Root, GraphSearchIntegrityRecipe.Request(state, selected, filtered), token);
        });
        ArgumentNullException.ThrowIfNull(failure);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(fault == GraphTraversalIntegrityFault.AdjacencyIdentity
            ? InvalidAdjacency : InvalidEdge);
        await Assert.That(result).IsNull();
        await GraphTraversalIntegrityAssertions.Image(store, image, cut);
    }

    internal static async Task ReceiptAsync(CommitReceipt expected, CommitReceipt actual)
    {
        await EqualAsync(expected, actual);
        await Assert.That(actual.Token).IsEqualTo(expected.Token);
        await Assert.That(actual.Mutations.All(mutation => mutation.CompositionReferences.IsEmpty)).IsTrue();
    }

    internal static byte[] Outcome(ZoneTreeStore store, CommandRequest command)
        => store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(command.Partition, P.Root, command.CommandId)))
            ?? throw new InvalidOperationException(MissingOutcome);

    internal static async Task ReplayAsync(DatabaseEngine owner, ZoneTreeStore store,
        CommandRequest command, CommitReceipt expected, byte[] original)
    {
        var image = CanonicalStoreImage.Bytes(store);
        var cut = store.Position;
        await ReceiptAsync(expected, GraphStorageReferenceFixture.Submit(owner, command).Get<CommitReceipt>());
        await Assert.That(Outcome(store, command).SequenceEqual(original)).IsTrue();
        await GraphTraversalIntegrityAssertions.Image(store, image, cut);
    }

    internal static async Task IdentityAsync(StoreIdentity expected, StoreIdentity actual)
    {
        await Assert.That(actual.FormatVersion).IsEqualTo(expected.FormatVersion);
        await Assert.That(actual.KeyCodecVersion).IsEqualTo(expected.KeyCodecVersion);
        await Assert.That(actual.NodeId).IsEqualTo(expected.NodeId);
        await Assert.That(actual.Incarnation).IsEqualTo(expected.Incarnation);
        await Assert.That(actual.SigningKey.Span.SequenceEqual(expected.SigningKey.Span)).IsTrue();
        await Assert.That(actual.Durability).IsEqualTo(expected.Durability);
        await Assert.That(actual.DispatchPaused).IsEqualTo(expected.DispatchPaused);
        await Assert.That(actual.ReadGeneration).IsEqualTo(expected.ReadGeneration);
        await Assert.That(actual.MinimumReaderContract).IsEqualTo(expected.MinimumReaderContract);
    }

    private static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
}
