using System.Text.Json;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class SubscriptionValueAssertions
{
    // Values retain exact ordered fields and user JSON. Execution wire bytes also encode
    // reference identity, so independently constructed equivalent graphs may differ.
    internal static async Task EqualAsync<T>(T actual, T expected)
    {
        await Assert.That(Project(actual)).IsEqualTo(Project(expected));
    }

    private static string Project<T>(T value)
    {
        if (value is not CommitReceipt receipt)
        {
            return JsonSerializer.Serialize(value, JsonDefaults.Options);
        }
        return JsonSerializer.Serialize(new
        {
            Receipt = receipt,
            CompositionReferences = receipt.Mutations.Select(mutation => mutation.CompositionReferences).ToArray()
        }, JsonDefaults.Options);
    }
}
