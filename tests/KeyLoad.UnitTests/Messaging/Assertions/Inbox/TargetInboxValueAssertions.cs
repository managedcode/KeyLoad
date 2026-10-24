using System.Collections.Immutable;
using System.Text.Json;

namespace KeyLoad.UnitTests.Messaging;

internal static class TargetInboxValueAssertions
{
    // Compare full semantic values: execution wire frames also preserve object-reference
    // topology, while an independently authored literal need not share that graph.
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(Project(actual)).IsEqualTo(Project(expected));

    private static string Project<T>(T value)
    {
        if (value is CommitReceipt receipt)
        {
            return JsonSerializer.Serialize(new
            {
                Receipt = receipt,
                CompositionReferences = receipt.Mutations.Select(mutation => mutation.CompositionReferences).ToArray()
            }, JsonDefaults.Options);
        }
        if (value is MutationReceipt singleMutation)
        {
            return JsonSerializer.Serialize(new
            {
                Receipt = singleMutation,
                singleMutation.CompositionReferences
            }, JsonDefaults.Options);
        }
        if (value is ImmutableArray<MutationReceipt> mutations)
        {
            return JsonSerializer.Serialize(new
            {
                Mutations = mutations,
                CompositionReferences = mutations.Select(mutation => mutation.CompositionReferences).ToArray()
            }, JsonDefaults.Options);
        }
        return JsonSerializer.Serialize(value, JsonDefaults.Options);
    }
}
