using KeyLoad.FunctionalTests.Messaging;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.BackupRestore;

internal static class MixedEventingFailurePostimage
{
    private const long SingleFailureCommit = 1;

    internal static async Task RequireAsync(TestDatabase source, QueueLifecycleTestState state,
        Command<DatabaseCommand> operation, string[] corruptRaw, byte[] restoredKey, byte[] restoredValue,
        byte[] outcomeKey, long repairedCut, long appliedIndex)
    {
        var locator = KeySpace.OutcomeLocator(state.Partition, operation.UserId!, operation.CommandId);
        var admitted = new[] { outcomeKey, locator, KeySpace.AppliedBytes, KeySpace.LastCommandBytes, KeySpace.ClockBytes }
            .Select(static key => Convert.ToHexString(key) + ":").ToArray();
        var original = corruptRaw.Append(Convert.ToHexString(restoredKey) + ":" + Convert.ToHexString(restoredValue));
        await Assert.That(WithoutMetadata(QueueWholeFlowStorage.Bytes(source.Store), admitted))
            .IsEquivalentTo(WithoutMetadata(original, admitted), CollectionOrdering.Matching);
        await Assert.That(source.Store.Position).IsEqualTo(checked(repairedCut + SingleFailureCommit));
        await Assert.That(source.Store.Read(view => view.ReadOwnedValue(locator)))
            .IsEquivalentTo(outcomeKey, CollectionOrdering.Matching);
        await Assert.That(source.Store.Read(view => OrleansSerialization.Deserialize<byte[]>(view.ReadOwnedValue(KeySpace.LastCommandBytes)!)))
            .IsEquivalentTo(outcomeKey, CollectionOrdering.Matching);
        await Assert.That(source.Store.Read(view => OrleansSerialization.Deserialize<long>(view.ReadOwnedValue(KeySpace.AppliedBytes)!)))
            .IsEqualTo(appliedIndex);
        await Assert.That(QueueWholeFlowStorage.Clock(source.Store)).IsEqualTo(new DateTimeOffset(operation.Timestamp));
    }

    private static string[] WithoutMetadata(IEnumerable<string> rows, string[] admitted)
        => rows.Where(row => !admitted.Any(key => row.StartsWith(key, StringComparison.Ordinal)))
            .OrderBy(static row => row, StringComparer.Ordinal).ToArray();
}
