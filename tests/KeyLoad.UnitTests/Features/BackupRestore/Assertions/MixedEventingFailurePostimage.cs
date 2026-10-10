using KeyLoad.Core;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class MixedEventingFailurePostimage
{
    private const long SingleFailureCommit = 1;

    internal static async Task RequireAsync(TestDatabase source, QueueLifecycleTestState state,
        ReplicatedOperation operation, string[] corruptRaw, byte[] restoredKey, byte[] restoredValue,
        byte[] outcomeKey, long repairedCut, long appliedIndex)
    {
        var locator = KeySpace.OutcomeLocatorV2(state.Partition, operation.PrincipalId, operation.Id);
        var admitted = new[] { outcomeKey, locator, KeySpace.AppliedBytes, KeySpace.ClockBytes }
            .Select(static key => Convert.ToHexString(key) + ":").ToArray();
        var original = corruptRaw.Append(Convert.ToHexString(restoredKey) + ":" + Convert.ToHexString(restoredValue));
        await Assert.That(WithoutMetadata(QueueWholeFlowStorage.Bytes(source.Store), admitted))
            .IsEquivalentTo(WithoutMetadata(original, admitted), CollectionOrdering.Matching);
        await Assert.That(source.Store.Position).IsEqualTo(checked(repairedCut + SingleFailureCommit));
        await Assert.That(source.Store.Read(view => view.ReadOwnedValue(locator)))
            .IsEquivalentTo(outcomeKey, CollectionOrdering.Matching);
        await Assert.That(source.Store.Read(view => NativeSerialization.Deserialize<long>(view.ReadOwnedValue(KeySpace.AppliedBytes)!)))
            .IsEqualTo(appliedIndex);
        await Assert.That(QueueWholeFlowStorage.Clock(source.Store)).IsEqualTo(operation.EvaluatedAt);
    }

    private static string[] WithoutMetadata(IEnumerable<string> rows, string[] admitted)
        => rows.Where(row => !admitted.Any(key => row.StartsWith(key, StringComparison.Ordinal)))
            .OrderBy(static row => row, StringComparer.Ordinal).ToArray();
}
