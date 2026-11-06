using KeyLoad.FunctionalTests.Messaging;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.BackupRestore;

internal static class MixedEventingFailurePostimage
{
    internal static async Task RequireAsync(TestDatabase source, QueueLifecycleTestState state,
        Command<DatabaseCommand> operation, string[] corruptRaw, byte[] restoredKey, byte[] restoredValue,
        byte[] outcomeKey, long repairedCut, long appliedIndex)
    {
        var locator = KeySpace.OutcomeLocator(state.Partition, operation.UserId!, operation.CommandId);
        var admitted = new[] { outcomeKey, locator, StorageCommandKeys.Applied(state.Partition), StorageCommandKeys.Clock(state.Partition) }
            .Select(static key => Convert.ToHexString(key) + ":").ToArray();
        var original = corruptRaw.Append(Convert.ToHexString(restoredKey) + ":" + Convert.ToHexString(restoredValue));
        await Assert.That(WithoutMetadata(QueueWholeFlowStorage.Bytes(source.Store), admitted))
            .IsEquivalentTo(WithoutMetadata(original, admitted), CollectionOrdering.Matching);
        await Assert.That(source.Store.Position).IsGreaterThan(repairedCut);
        await Assert.That(source.Store.Read(view => view.ReadOwnedValue(locator)))
            .IsEquivalentTo(outcomeKey, CollectionOrdering.Matching);
        await Assert.That(source.Store.Read(view => StorageOperationApply.ReadCanonicalSequence(view, state.Partition)))
            .IsEqualTo(appliedIndex);
        await Assert.That(QueueWholeFlowStorage.Clock(source.Store, source.Partition)).IsEqualTo(new DateTimeOffset(operation.Timestamp));
    }

    private static string[] WithoutMetadata(IEnumerable<string> rows, string[] admitted)
        => rows.Where(row => !admitted.Any(key => row.StartsWith(key, StringComparison.Ordinal)))
            .OrderBy(static row => row, StringComparer.Ordinal).ToArray();
}
