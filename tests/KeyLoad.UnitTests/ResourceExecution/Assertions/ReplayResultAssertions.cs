using System.Text.Json;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.ResourceExecution;

// Structural replay assertions preserve ordered fields and exact user JSON strings without
// assuming ImmutableArray backing identity or equal execution reference graphs.
internal static class ReplayResultAssertions
{
    internal static async Task BytesAsync(byte[] expected, byte[] actual)
        => await Assert.That(actual.AsSpan().SequenceEqual(expected)).IsTrue();

    internal static Task SameAsync(Result<object> expected, Result<object> actual)
        => BytesAsync(OrleansSerialization.Serialize(expected), OrleansSerialization.Serialize(actual));

    internal static async Task Same<T>(Result<object> actual, Result<object> expected) where T : class
    {
        await Assert.That(Errors.Code(actual.Problem).Value).IsEqualTo(Errors.Code(expected.Problem).Value);
        await Assert.That(actual.Problem?.Detail).IsEqualTo(expected.Problem?.Detail);
        await Assert.That(actual.Value?.GetType()).IsEqualTo(typeof(T));
        await Assert.That(expected.Value?.GetType()).IsEqualTo(typeof(T));
        await Assert.That(Project(actual.Get<T>())).IsEqualTo(Project(expected.Get<T>()));
    }

    private static string Project<T>(T value)
    {
        if (value is not CommitReceipt receipt)
        {
            return JsonSerializer.Serialize(value, JsonDefaults.Options);
        }
        // The private execution composition field is deliberately excluded from public receipt JSON.
        // Include it explicitly so every serialized owning receipt field participates in equality.
        return JsonSerializer.Serialize(new
        {
            Receipt = receipt,
            CompositionReferences = receipt.Mutations.Select(mutation => mutation.CompositionReferences).ToArray()
        }, JsonDefaults.Options);
    }
}
