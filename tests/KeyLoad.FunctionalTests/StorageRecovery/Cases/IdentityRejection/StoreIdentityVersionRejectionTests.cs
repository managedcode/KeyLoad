using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class StoreIdentityVersionRejectionTests
{
    private const int UnsupportedIdentityVersion = 9;

    [Test]
    public async Task UnsupportedIdentityVersionFailsBeforeTreeMutation()
    {
        using var files = new StoreFileFixture();
        StoreIdentity originalIdentity;
        long originalPosition;
        using (var store = new ZoneTreeStore(new(files.DirectoryPath), UnitExecutionOptions.StorageExecution()))
        {
            store.Commit((transaction, _) => { transaction.Put([0x10], [0x30]); return true; });
            originalIdentity = store.Identity;
            originalPosition = store.Position;
        }
        var token = TestContext.Current!.Execution.CancellationToken;
        var identityBytes = await File.ReadAllBytesAsync(files.IdentityPath, token);
        await files.WriteIdentityAsync(originalIdentity with
        {
            FormatVersion = UnsupportedIdentityVersion
        });
        var before = await files.CaptureFilesAsync();
        await files.AssertRejectedAsync(await File.ReadAllBytesAsync(files.IdentityPath), ErrorCode.FormatUnsupported);
        await files.AssertFilesUnchangedAsync(before);
        await File.WriteAllBytesAsync(files.IdentityPath, identityBytes, token);
        await Assert.That(await File.ReadAllBytesAsync(files.IdentityPath, token))
            .IsEquivalentTo(identityBytes, CollectionOrdering.Matching);
        await IdentityRepairContinuation.RequireCurrentAsync(files, originalIdentity, originalPosition,
            token).ConfigureAwait(false);
    }
}
