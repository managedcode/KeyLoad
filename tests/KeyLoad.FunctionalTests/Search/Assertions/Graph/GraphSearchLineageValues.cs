using System.Text.Json;
using KeyLoad.FunctionalTests.GraphTraversal;
using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.Search;

internal static class GraphSearchLineageValues
{
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(actual, JsonDefaults.Options),
            JsonSerializer.SerializeToElement(expected, JsonDefaults.Options))).IsTrue();
    internal static async Task ReceiptAsync(CommitReceipt actual, CommitReceipt expected)
    {
        await EqualAsync(actual, expected);
        foreach (var mutation in actual.Mutations)
        {
            await Assert.That(mutation.CompositionReferences).IsEmpty();
        }
    }
    internal static async Task HealthyAsync(GraphSearchLineageFixture state)
        => await GraphLabelUseOperations.ObserveAsync(state.Store, async () =>
        {
            var result = await new SearchEngine(state.Owner, UnitExecutionOptions.QueryExecution()).GraphSearchAsync(
                GraphSearchLineageProtocol.Reader, state.Request, TestContext.Current!.Execution.CancellationToken);
            await EqualAsync(result, GraphSearchLineageRecipe.Expected(state.Harness, state.Expansion, state.EmptyLabels));
        });
    internal static async Task RefusedAsync(GraphSearchLineageFixture state, ErrorCode code, string? detail)
        => await GraphLabelUseOperations.ObserveAsync(state.Store, async () =>
        {
            GraphSearchResult? result = null;
            var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () =>
            {
                result = await new SearchEngine(state.Owner, UnitExecutionOptions.QueryExecution()).GraphSearchAsync(
                    GraphSearchLineageProtocol.Reader, state.Request, TestContext.Current!.Execution.CancellationToken);
            });
            await Assert.That(failure!.Code).IsEqualTo(code);
            if (detail is not null)
            {
                await Assert.That(failure.Message).IsEqualTo(detail);
            }
            await Assert.That(result).IsNull();
        });
    internal static async Task ReplayAsync(GraphSearchLineageFixture state)
        => await GraphLabelUseOperations.ObserveAsync(state.Store, async () =>
        {
            await ReceiptAsync(state.Owner.ApplyEmbedded(state.Operation(), TestContext.Current!.Execution.CancellationToken).Get<CommitReceipt>(), state.Receipt);
            await Assert.That(state.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(state.Partition,
                GraphSearchLineageProtocol.Root, state.Seed.CommandId)))).IsEquivalentTo(state.Outcome, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            var result = await new SearchEngine(state.Owner, UnitExecutionOptions.QueryExecution()).GraphSearchAsync(
                GraphSearchLineageProtocol.Reader, state.Request, TestContext.Current!.Execution.CancellationToken);
            await EqualAsync(result, GraphSearchLineageRecipe.Expected(state.Harness, state.Expansion, state.EmptyLabels));
        });
}
