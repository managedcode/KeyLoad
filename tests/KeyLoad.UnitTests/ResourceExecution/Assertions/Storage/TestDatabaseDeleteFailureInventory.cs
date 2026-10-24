using KeyLoad.UnitTests.StorageRecovery;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.ResourceExecution;

internal static class TestDatabaseDeleteFailureInventory
{
    internal static async Task RequireAsync(TestDatabase fixture, PartitionIdentityInventoryCut expected,
        CancellationToken token)
    {
        var actual = await TestDatabaseDeleteFailureFiles.CaptureAsync(fixture, token);
        await Assert.That(actual.Bytes.Keys.Order(StringComparer.Ordinal))
            .IsEquivalentTo(expected.Bytes.Keys.Order(StringComparer.Ordinal), CollectionOrdering.Matching);
        foreach (var (path, bytes) in expected.Bytes)
        {
            if (bytes is null)
            {
                await Assert.That(actual.Bytes[path]).IsNull();
            }
            else
            {
                await Assert.That(actual.Bytes[path]!.AsSpan().SequenceEqual(bytes)).IsTrue();
            }
        }
        await Assert.That(actual.Modes.Keys.Order(StringComparer.Ordinal))
            .IsEquivalentTo(expected.Modes.Keys.Order(StringComparer.Ordinal), CollectionOrdering.Matching);
        foreach (var (path, mode) in expected.Modes)
        {
            await Assert.That(actual.Modes[path]).IsEqualTo(mode);
        }
    }
}
