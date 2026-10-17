using System.Text.Json;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueueViewLeaseIsolationResults
{
    internal static async Task SameAsync(OperationResult actual, OperationResult expected)
    {
        await Assert.That(actual.Error).IsEqualTo(expected.Error);
        await Assert.That(actual.Json == expected.Json && actual.SafeDetail == expected.SafeDetail).IsTrue();
        await Assert.That(actual.NativeValue?.GetType() == expected.NativeValue?.GetType()).IsTrue();
        await Assert.That(Project(actual.NativeValue).AsSpan().SequenceEqual(Project(expected.NativeValue))).IsTrue();
    }

    private static byte[] Project(object? value)
        => value is CommitReceipt receipt ? JsonSerializer.SerializeToUtf8Bytes(new
        {
            Receipt = receipt,
            CompositionReferences = receipt.Mutations.Select(item => item.CompositionReferences).ToArray()
        }, JsonDefaults.Options) : JsonSerializer.SerializeToUtf8Bytes(value, JsonDefaults.Options);

    internal static async Task FailureAsync(OperationResult actual, ErrorCode code)
    {
        await Assert.That(actual.Error).IsEqualTo(code);
        await Assert.That(actual.Json is null && actual.NativeValue is null).IsTrue();
    }
}
