using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal static class BlobPriorPublicationRefusal
{
    internal static async Task AssertAsync(DatabaseEngine owner, ZoneTreeStore store, BlobPriorPublicationState seed,
        Command<DatabaseCommand> delete, Command<DatabaseCommand> reclaim, string[] image, long position)
    {
        var operations = new BlobStorageOperations(owner);
        await ErrorAsync(() => operations.Metadata("root", new(seed.Blob)));
        await ErrorAsync(() => operations.Read("root", new(seed.Blob, 1, 0, 4)));
        await ErrorAsync(() => owner.Apply(delete));
        await ErrorAsync(() => owner.Apply(reclaim));
        await Assert.That(OutcomeStoreOracle.Read(store, delete)).IsNull();
        await Assert.That(OutcomeStoreOracle.Read(store, reclaim)).IsNull();
        var replay = owner.ResolveOutcome(seed.Commands[2].Operation);
        await Assert.That(Errors.Code(replay.Problem)).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(replay.Problem?.Detail).IsEqualTo("The persisted blob records are inconsistent.");

        await Assert.That(replay.Value).IsNull();
        await BlobPriorPublicationImage.SameAsync(store, image, position, seed.Identity);
        await BlobPriorPublicationAssertions.AuthorityAsync(owner, seed);
    }

    private static async Task ErrorAsync(Action operation)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(operation);
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(error.Message).IsEqualTo("The persisted blob records are inconsistent.");
    }

    internal static async Task InvalidatedAsync(DatabaseEngine owner, BlobPriorPublicationCommand command)
    {
        var result = owner.ResolveOutcome(command.Operation);
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(result.Problem?.Detail).IsEqualTo("The selected upload lifetime is unavailable.");

        await Assert.That(result.Value).IsNull();
    }
}
