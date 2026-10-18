using ManagedCode.Communication;
using System.Text.Json;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueueViewLeaseIsolationResults
{
    internal static async Task SameAsync(Result<object> actual, Result<object> expected)
    {
        await Assert.That(Errors.Code(actual.Problem)).IsEqualTo(Errors.Code(expected.Problem));
        await Assert.That(actual.Json == expected.Json && actual.Problem?.Detail == expected.Problem?.Detail).IsTrue();
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
        await Assert.That(Errors.Code(actual.Problem)).IsEqualTo(code);
        await Assert.That(actual.Json is null && actual.Value is null).IsTrue();
    }
}
