using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobRetiredPartRefusal
{
    internal static async Task AssertAsync(DatabaseEngine owner, ZoneTreeStore store, BlobRetiredPartState seed,
        Command<DatabaseCommand> reclaim, string[] image, long position)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(() => owner.Apply(reclaim));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(error.Message).IsEqualTo("The persisted blob records are inconsistent.");
        await Assert.That(OutcomeStoreOracle.Read(store, reclaim)).IsNull();
        await BlobRetiredPartAssertions.TombstoneAsync(owner, seed);
        await BlobPriorPublicationImage.SameAsync(store, image, position, seed.Publication.Identity);
    }
}
