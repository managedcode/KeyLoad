using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class ZoneTreeExistingStoreValueBoundaryTests
{
    private const int ReturnedValueByteLimit = 256;
    private const int RejectedValueByteCount = 257;
    private const string InvalidDataExceptionType = "System.IO.InvalidDataException";

    [Test]
    public async Task AcSg009P003ChildReturnsTheCompleteMaximumSizedValueAndStoreReopensUnchanged()
    {
        using var files = new ZoneTreeExistingStoreFixture();
        var expectedValue = CreateValue(ReturnedValueByteLimit);
        var position = CommitValue(files, expectedValue);
        var identityBytes = await File.ReadAllBytesAsync(files.IdentityPath);
        var result = await files.InspectAsync();
        await ExistingStoreInspectionAssertions.SucceededAsync(result, files.Identity, expectedValue, position);
        await Assert.That(await File.ReadAllBytesAsync(files.IdentityPath)).IsEquivalentTo(identityBytes, CollectionOrdering.Matching);
        using var reopened = new ZoneTreeStore(files.Options, UnitExecutionOptions.StorageExecution());
        await NativeStoreAssertions.ReopenedAsync(reopened, position);
        await Assert.That(reopened.Read(view => view.ReadOwnedValue(files.Key))).IsEquivalentTo(expectedValue, CollectionOrdering.Matching);
        await Assert.That(await File.ReadAllBytesAsync(files.IdentityPath)).IsEquivalentTo(identityBytes, CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcSg009P003ChildRejectsOversizedValueWithoutTruncationAndStoreReopensUnchanged()
    {
        using var files = new ZoneTreeExistingStoreFixture();
        var expectedValue = CreateValue(RejectedValueByteCount);
        var position = CommitValue(files, expectedValue);
        var identityBytes = await File.ReadAllBytesAsync(files.IdentityPath);
        var result = await files.InspectAsync();
        await ExistingStoreInspectionAssertions.FailedAsync(result, null, InvalidDataExceptionType);
        await Assert.That(result.Receipt!.Value).IsNull();
        await Assert.That(await File.ReadAllBytesAsync(files.IdentityPath)).IsEquivalentTo(identityBytes, CollectionOrdering.Matching);
        using var reopened = new ZoneTreeStore(files.Options, UnitExecutionOptions.StorageExecution());
        await NativeStoreAssertions.ReopenedAsync(reopened, position);
        await Assert.That(reopened.Read(view => view.ReadOwnedValue(files.Key))).IsEquivalentTo(expectedValue, CollectionOrdering.Matching);
        await Assert.That(await File.ReadAllBytesAsync(files.IdentityPath)).IsEquivalentTo(identityBytes, CollectionOrdering.Matching);
    }

    private static long CommitValue(ZoneTreeExistingStoreFixture files, byte[] value)
    {
        using var store = new ZoneTreeStore(files.Options, UnitExecutionOptions.StorageExecution());
        store.Commit((transaction, _) => { transaction.Put(files.Key, value); return true; });
        return store.Position;
    }

    private static byte[] CreateValue(int length)
        => Enumerable.Range(0, length).Select(static value => (byte)value).ToArray();
}
