using System.Text;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class StorageOwnerProcessTests
{
    private const long InitialPosition = 1;
    private const long UpdatedPosition = 2;
    private const string UpdatedValueText = "owner process update survives every open";
    private const string MissingValueMessage = "The parent store lost its committed value.";

    [Test]
    public async Task AcStorageOwner001SeparateProcessCannotOpenLiveOwnerAndHealthyReopenPreservesUpdatedState()
    {
        using var files = new ZoneTreeExistingStoreFixture();
        var expectedValue = Encoding.UTF8.GetBytes(UpdatedValueText);
        using var store = new ZoneTreeStore(files.Options, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var identity = store.Identity;
        var identityBytes = await File.ReadAllBytesAsync(files.IdentityPath);
        var initialJournalBytes = await File.ReadAllBytesAsync(files.JournalPath);

        await AssertParentStateAsync(store, files, files.Value, InitialPosition);
        var firstDenial = await files.InspectAsync();
        await AssertDeniedAsync(firstDenial);
        await AssertCanonicalBytesAsync(files, identityBytes, initialJournalBytes);
        await AssertParentStateAsync(store, files, files.Value, InitialPosition);

        var commitPosition = store.Commit((transaction, position) =>
        {
            transaction.Put(files.Key, expectedValue);
            return position;
        });
        await Assert.That(commitPosition).IsEqualTo(UpdatedPosition);
        await AssertParentStateAsync(store, files, expectedValue, UpdatedPosition);

        var updatedJournalBytes = await File.ReadAllBytesAsync(files.JournalPath);
        var secondDenial = await files.InspectAsync();
        await AssertDeniedAsync(secondDenial);
        await AssertCanonicalBytesAsync(files, identityBytes, updatedJournalBytes);
        await AssertParentStateAsync(store, files, expectedValue, UpdatedPosition);

        store.Dispose();
        var healthyChild = await files.InspectAsync();
        await ExistingStoreInspectionAssertions.SucceededAsync(healthyChild, identity, expectedValue, UpdatedPosition);
        await AssertCanonicalBytesAsync(files, identityBytes, updatedJournalBytes);

        using var reopened = new ZoneTreeStore(files.Options, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        await AssertIdentityMatchesAsync(identity, reopened.Identity);
        await AssertParentStateAsync(reopened, files, expectedValue, UpdatedPosition);
        await AssertCanonicalBytesAsync(files, identityBytes, updatedJournalBytes);
    }

    private static async Task AssertDeniedAsync(ExistingStoreInspectorExit result)
    {
        await ExistingStoreInspectionAssertions.FailedAsync(result, null, ExistingStoreInspectionExpectedFailures.Io);
        await Assert.That(result.Receipt!.Success).IsFalse();
    }

    private static async Task AssertCanonicalBytesAsync(ZoneTreeExistingStoreFixture files,
        byte[] expectedIdentity, byte[] expectedJournal)
    {
        await Assert.That(await File.ReadAllBytesAsync(files.IdentityPath))
            .IsEquivalentTo(expectedIdentity, CollectionOrdering.Matching);
        await Assert.That(await File.ReadAllBytesAsync(files.JournalPath))
            .IsEquivalentTo(expectedJournal, CollectionOrdering.Matching);
    }

    private static async Task AssertParentStateAsync(ZoneTreeStore store, ZoneTreeExistingStoreFixture files,
        byte[] expectedValue, long expectedPosition)
    {
        var value = store.Read(view => view.ReadOwnedValue(files.Key))
            ?? throw new InvalidOperationException(MissingValueMessage);
        await Assert.That(value).IsEquivalentTo(expectedValue, CollectionOrdering.Matching);
        await Assert.That(store.Position).IsEqualTo(expectedPosition);
    }

    private static async Task AssertIdentityMatchesAsync(StoreIdentity expected, StoreIdentity actual)
    {
        await Assert.That(actual.NodeId).IsEqualTo(expected.NodeId);
        await Assert.That(actual.Incarnation).IsEqualTo(expected.Incarnation);
        await Assert.That(actual.FormatVersion).IsEqualTo(expected.FormatVersion);
        await Assert.That(actual.KeyCodecVersion).IsEqualTo(expected.KeyCodecVersion);
        await Assert.That(actual.SigningKey.Span.SequenceEqual(expected.SigningKey.Span)).IsTrue();
        await Assert.That(actual.Durability).IsEqualTo(expected.Durability);
        await Assert.That(actual.DispatchPaused).IsEqualTo(expected.DispatchPaused);
        await Assert.That(actual.ReadGeneration).IsEqualTo(expected.ReadGeneration);
        await Assert.That(actual.MinimumReaderContract).IsEqualTo(expected.MinimumReaderContract);
    }
}
