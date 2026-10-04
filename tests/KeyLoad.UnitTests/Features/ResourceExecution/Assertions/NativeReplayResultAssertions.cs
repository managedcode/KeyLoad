using System.Text.Json;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

// Structural replay assertions preserve ordered fields and exact user JSON strings without
// assuming ImmutableArray backing identity or equal native reference graphs.
internal static class NativeReplayResultAssertions
{
    internal static async Task Same<T>(OperationResult actual, OperationResult expected) where T : class
    {
        await Assert.That(actual.Json).IsEqualTo(expected.Json);
        await Assert.That(actual.Error).IsEqualTo(expected.Error);
        await Assert.That(actual.SafeDetail).IsEqualTo(expected.SafeDetail);
        await Assert.That(actual.NativeValue?.GetType()).IsEqualTo(typeof(T));
        await Assert.That(expected.NativeValue?.GetType()).IsEqualTo(typeof(T));
        await Assert.That(Project(actual.Get<T>())).IsEqualTo(Project(expected.Get<T>()));
    }

    private static string Project<T>(T value)
    {
        if (value is not CommitReceipt receipt)
        {
            return JsonSerializer.Serialize(value, JsonDefaults.Options);
        }
        // The private native composition field is deliberately excluded from public receipt JSON.
        // Include it explicitly so every serialized owning receipt field participates in equality.
        return JsonSerializer.Serialize(new
        {
            Receipt = receipt,
            CompositionReferences = receipt.Mutations.Select(mutation => mutation.CompositionReferences).ToArray()
        }, JsonDefaults.Options);
    }
}
