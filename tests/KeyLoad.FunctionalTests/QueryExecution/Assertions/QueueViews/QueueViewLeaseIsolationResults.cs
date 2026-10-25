using System.Text.Json;
using ManagedCode.Communication;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class QueueViewLeaseIsolationResults
{
    internal static async Task SameAsync(Result<object> actual, Result<object> expected)
    {
        await Assert.That(Errors.Code(actual.Problem).Value).IsEqualTo(Errors.Code(expected.Problem).Value);
        await Assert.That(OrleansSerialization.Serialize(actual).AsSpan().SequenceEqual(OrleansSerialization.Serialize(expected))).IsTrue();
        await Assert.That(actual.Value?.GetType() == expected.Value?.GetType()).IsTrue();
        await Assert.That(Project(actual.Value).AsSpan().SequenceEqual(Project(expected.Value))).IsTrue();
    }

    private static byte[] Project(object? value)
        => value is CommitReceipt receipt ? JsonSerializer.SerializeToUtf8Bytes(new
        {
            Receipt = receipt,
            CompositionReferences = receipt.Mutations.Select(item => item.CompositionReferences).ToArray()
        }, JsonDefaults.Options) : JsonSerializer.SerializeToUtf8Bytes(value, JsonDefaults.Options);

    internal static async Task FailureAsync(Result<object> actual, ErrorCode code)
    {
        await Assert.That(Errors.Code(actual.Problem).Value).IsEqualTo(code);
        await Assert.That(actual.Value is null).IsTrue();
    }
}
